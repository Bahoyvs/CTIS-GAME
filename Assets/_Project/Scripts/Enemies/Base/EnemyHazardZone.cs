using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using CBuilding.Core;
using CBuilding.Heroes;
using CBuilding.StatusEffects;

namespace CBuilding.Enemies
{
    /// <summary>
    /// Server-authoritative hostile ground zone — the enemy-side mirror of
    /// AreaOfEffectNetworked. Spit Bile's slowing micro-puddle, Bile-Vomiter's 5s
    /// corrosive pool. Ticks damage (flagged DoT|Hazard: no knockback/hitstun spam)
    /// and/or applies a status effect to heroes standing inside; despawns after
    /// its duration.
    ///
    /// PREFAB: this + NetworkObject + child visual, registered in Network Prefabs.
    /// Shape/damage are authored per-prefab in the Inspector (one prefab per puddle
    /// type); spawners only pass the instigator via ServerInit before Spawn().
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class EnemyHazardZone : NetworkBehaviour
    {
        [Header("Shape & Lifetime")]
        [Min(0.1f)] [SerializeField] private float radius = 1.2f;
        [Min(0.1f)] [SerializeField] private float duration = 3f;
        [Min(0.1f)] [SerializeField] private float tickInterval = 0.5f;

        [Header("Per Tick")]
        [Tooltip("0 = no damage (pure debuff zone, e.g. Spit Bile's slow puddle).")]
        [Min(0f)] [SerializeField] private float damagePerTick = 0f;

        [Tooltip("Applied to every hero inside on every tick. Refresh-stacking effects last " +
                 "their full duration after leaving.")]
        [SerializeField] private EffectDataSO appliedEffect;

        private GameObject _instigator;
        private float _elapsed;
        private float _nextTickTime;
        private readonly List<BaseHero> _pulseScratch = new();

        /// <summary>Server-side, call BEFORE NetworkObject.Spawn().</summary>
        public void ServerInit(GameObject instigator) => _instigator = instigator;

        public override void OnNetworkSpawn()
        {
            if (!IsServer) enabled = false; // Clients keep the visual only.
        }

        private void Update()
        {
            // (Server-only.)
            _elapsed += Time.deltaTime;

            if (Time.time >= _nextTickTime)
            {
                _nextTickTime = Time.time + tickInterval;
                Pulse();
            }

            if (_elapsed >= duration && NetworkObject.IsSpawned)
                NetworkObject.Despawn(true);
        }

        private void Pulse()
        {
            _pulseScratch.Clear();
            foreach (BaseHero hero in BaseHero.ActiveHeroes)
            {
                if (hero == null || !hero.IsAlive) continue;
                Vector3 delta = hero.transform.position - transform.position;
                delta.y = 0f;
                if (delta.sqrMagnitude <= radius * radius) _pulseScratch.Add(hero);
            }

            foreach (BaseHero hero in _pulseScratch)
            {
                if (damagePerTick > 0f)
                {
                    hero.TakeDamage(new DamageInfo(
                        damagePerTick, hero.transform.position, Vector3.zero, 0f, _instigator,
                        DamageFlags.DoT | DamageFlags.Hazard));
                }

                if (appliedEffect != null && hero.TryGetComponent(out StatusEffectController status))
                    status.ApplyEffect(appliedEffect, _instigator);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.5f, 1f, 0.1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
#endif
    }
}
