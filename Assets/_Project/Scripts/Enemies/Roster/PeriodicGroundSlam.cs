using Unity.Netcode;
using UnityEngine;
using CBuilding.Core;
using CBuilding.Heroes;
using CBuilding.StatusEffects;

namespace CBuilding.Enemies.Roster
{
    /// <summary>
    /// The Ground-Slammer: every 8 seconds, a rectangular shockwave in front of it —
    /// damage + 1.5s stun (via EffectDataSO, so the hero controller's ControlFlags
    /// aggregation handles the actual disable). Independent of the normal attack loop:
    /// the slam fires whenever the timer is up AND the target is inside the rectangle,
    /// so it punishes face-tanking even mid-chase.
    /// </summary>
    [RequireComponent(typeof(RosterEnemy))]
    public class PeriodicGroundSlam : NetworkBehaviour
    {
        [Min(1f)] [SerializeField] private float slamInterval = 8f;

        [Header("Rectangle (from the enemy, toward its target)")]
        [Min(0.5f)] [SerializeField] private float length = 5f;
        [Min(0.5f)] [SerializeField] private float width = 3f;

        [Header("Impact")]
        [Min(0f)] [SerializeField] private float damage = 15f;
        [Min(0f)] [SerializeField] private float knockbackForce = 4f;
        [Tooltip("1.5s Stun EffectDataSO from the roster effect set.")]
        [SerializeField] private EffectDataSO stunEffect;

        private RosterEnemy _enemy;
        private float _nextSlamTime;

        private void Awake() => _enemy = GetComponent<RosterEnemy>();

        public override void OnNetworkSpawn()
        {
            if (!IsServer) { enabled = false; return; }
            enabled = true;
            _nextSlamTime = Time.time + slamInterval; // Never opens with a slam.
        }

        private void Update()
        {
            if (!_enemy.IsAlive || _enemy.IsSpawning || _enemy.BrainSuspended) return;
            if (Time.time < _nextSlamTime || _enemy.Target == null) return;

            Vector3 forward = _enemy.Target.transform.position - transform.position;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) return;
            forward.Normalize();

            // Only commit the slam when the current target would actually be caught.
            if (!InRectangle(forward, _enemy.Target.transform.position)) return;

            _nextSlamTime = Time.time + slamInterval;

            int victims = 0;
            foreach (BaseHero hero in BaseHero.ActiveHeroes)
            {
                if (hero == null || !hero.IsAlive || !InRectangle(forward, hero.transform.position))
                    continue;

                Vector3 knockDir = hero.transform.position - transform.position;
                hero.TakeDamage(new DamageInfo(
                    damage, hero.transform.position, knockDir, knockbackForce, gameObject,
                    DamageFlags.Melee));

                if (stunEffect != null && hero.TryGetComponent(out StatusEffectController status))
                    status.ApplyEffect(stunEffect, gameObject);
                victims++;
            }

            CombatLogManager.LogAction(_enemy.DisplayName, "used",
                $"Ground_Slam stunning {victims} hero(es)", transform.position);
        }

        private bool InRectangle(Vector3 forward, Vector3 worldPos)
        {
            Vector3 local = worldPos - transform.position;
            local.y = 0f;
            float forwardDist = Vector3.Dot(local, forward);
            if (forwardDist < 0f || forwardDist > length) return false;
            float lateral = (local - forward * forwardDist).magnitude;
            return lateral <= width * 0.5f;
        }
    }
}
