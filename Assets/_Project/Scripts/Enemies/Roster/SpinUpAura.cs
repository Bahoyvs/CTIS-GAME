using Unity.Netcode;
using UnityEngine;
using CBuilding.Core;
using CBuilding.Heroes;

namespace CBuilding.Enemies.Roster
{
    /// <summary>
    /// Juggernaut Blender: a spinning-blade aura that ticks damage to every hero in
    /// radius, scaling EXPONENTIALLY the longer the spin runs uninterrupted — and
    /// resetting to base whenever it takes a direct (non-DoT) hit or nobody is in
    /// range. The counterplay is written into the math: keep poking it, or leave.
    /// </summary>
    [RequireComponent(typeof(RosterEnemy))]
    public class SpinUpAura : NetworkBehaviour
    {
        [Min(0.5f)] [SerializeField] private float radius = 2.8f;
        [Min(0.1f)] [SerializeField] private float tickInterval = 0.5f;
        [Min(0f)] [SerializeField] private float baseDamagePerTick = 4f;

        [Tooltip("Damage multiplier applied per elapsed tick: dmg = base * growth^ticks.")]
        [Min(1f)] [SerializeField] private float growthPerTick = 1.15f;

        [Tooltip("Cap on the total multiplier so an ignored Blender is lethal, not infinite.")]
        [Min(1f)] [SerializeField] private float maxMultiplier = 10f;

        private RosterEnemy _enemy;
        private float _nextTickTime;
        private int _spinTicks;

        private void Awake() => _enemy = GetComponent<RosterEnemy>();

        public override void OnNetworkSpawn()
        {
            if (!IsServer) { enabled = false; return; }
            enabled = true;
            _spinTicks = 0;
            _enemy.OnDamaged += HandleDamaged;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) _enemy.OnDamaged -= HandleDamaged;
        }

        private void HandleDamaged(DamageInfo info)
        {
            // Direct hits interrupt the spin; DoT ticks don't (matches BaseEnemy hitstun rules).
            if (!info.IsHealing && (info.Flags & DamageFlags.DoT) == 0) _spinTicks = 0;
        }

        private void Update()
        {
            if (!_enemy.IsAlive || _enemy.IsSpawning || _enemy.BrainSuspended) return;
            if (Time.time < _nextTickTime) return;
            _nextTickTime = Time.time + tickInterval;

            bool hitSomeone = false;
            float multiplier = Mathf.Min(maxMultiplier, Mathf.Pow(growthPerTick, _spinTicks));
            float damage = baseDamagePerTick * multiplier;

            foreach (BaseHero hero in BaseHero.ActiveHeroes)
            {
                if (hero == null || !hero.IsAlive) continue;
                Vector3 delta = hero.transform.position - transform.position;
                delta.y = 0f;
                if (delta.sqrMagnitude > radius * radius) continue;

                hero.TakeDamage(new DamageInfo(
                    damage, hero.transform.position, delta, 0.5f, gameObject,
                    DamageFlags.Melee));
                hitSomeone = true;
            }

            // Spin builds only while it's actually blending someone; empty air resets it.
            _spinTicks = hitSomeone ? _spinTicks + 1 : 0;
        }
    }
}
