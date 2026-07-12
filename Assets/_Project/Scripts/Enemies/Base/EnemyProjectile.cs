using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using CBuilding.Core;
using CBuilding.Heroes;
using CBuilding.StatusEffects;

namespace CBuilding.Enemies
{
    /// <summary>
    /// Server-simulated enemy projectile. The server moves the transform (NetworkTransform
    /// replicates), tests hero proximity against BaseHero.ActiveHeroes (no physics layers to
    /// configure), applies damage + optional status effect, optionally drops a hazard puddle
    /// on impact/expiry, then Despawn(true)s itself.
    ///
    /// Piercing (Rail-Spitter): flies through every hero and obstacle, hitting each hero once.
    /// Non-piercing also ignores level geometry for now — arena corridors are open enough
    /// that wall-clipping shots are a tuning problem, not a correctness one (flagged in docs).
    ///
    /// PREFAB: this + NetworkObject + NetworkTransform + child visual. Registered in
    /// Network Prefabs. Configure via ServerInit BEFORE NetworkObject.Spawn().
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class EnemyProjectile : NetworkBehaviour
    {
        [Header("Collision")]
        [Tooltip("Hero counts as hit when within this XZ distance of the projectile.")]
        [Min(0.05f)] [SerializeField] private float hitRadius = 0.7f;

        [Tooltip("Hard lifetime cap, independent of range.")]
        [Min(0.5f)] [SerializeField] private float maxLifetime = 6f;

        // ---- Server-only, set via ServerInit before Spawn ----
        private Vector3 _direction;
        private float _speed;
        private float _damage;
        private bool _piercing;
        private EffectDataSO _onHitEffect;
        private EnemyHazardZone _impactPuddlePrefab;
        private GameObject _instigator;
        private float _maxRange;
        private float _groundY;

        private Vector3 _origin;
        private float _despawnTime;
        private readonly HashSet<BaseHero> _alreadyHit = new();

        /// <summary>Server-side, call BEFORE NetworkObject.Spawn(). Plain fields — never replicated.</summary>
        public void ServerInit(Vector3 direction, float speed, float damage, bool piercing,
                               EffectDataSO onHitEffect, EnemyHazardZone impactPuddlePrefab,
                               GameObject instigator, float maxRange, float groundY)
        {
            _direction = direction.normalized;
            _speed = speed;
            _damage = damage;
            _piercing = piercing;
            _onHitEffect = onHitEffect;
            _impactPuddlePrefab = impactPuddlePrefab;
            _instigator = instigator;
            _maxRange = maxRange;
            _groundY = groundY;
            _origin = transform.position;
            _despawnTime = Time.time + maxLifetime;
            _alreadyHit.Clear();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) enabled = false; // Clients: NetworkTransform moves the visual.
        }

        private void Update()
        {
            // (Server-only: enabled=false everywhere else.)
            transform.position += _direction * (_speed * Time.deltaTime);

            foreach (BaseHero hero in BaseHero.ActiveHeroes)
            {
                if (hero == null || !hero.IsAlive || _alreadyHit.Contains(hero)) continue;

                Vector3 delta = hero.transform.position - transform.position;
                delta.y = 0f;
                if (delta.sqrMagnitude > hitRadius * hitRadius) continue;

                _alreadyHit.Add(hero);
                hero.TakeDamage(new DamageInfo(
                    _damage, transform.position, _direction, 1.5f, _instigator));

                if (_onHitEffect != null && hero.TryGetComponent(out StatusEffectController status))
                    status.ApplyEffect(_onHitEffect, _instigator);

                if (!_piercing)
                {
                    Expire(spawnPuddle: true);
                    return;
                }
            }

            bool rangeSpent = (transform.position - _origin).sqrMagnitude >= _maxRange * _maxRange;
            if (rangeSpent || Time.time >= _despawnTime)
                Expire(spawnPuddle: rangeSpent); // Puddles drop where the shot lands, not on timeout mid-air.
        }

        private void Expire(bool spawnPuddle)
        {
            if (spawnPuddle && _impactPuddlePrefab != null)
            {
                Vector3 pos = new(transform.position.x, _groundY, transform.position.z);
                EnemyHazardZone puddle = Instantiate(_impactPuddlePrefab, pos, Quaternion.identity);
                puddle.ServerInit(_instigator);
                puddle.NetworkObject.Spawn(true);
            }

            if (NetworkObject.IsSpawned) NetworkObject.Despawn(true);
        }
    }
}
