using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Abilities.Delivery
{
    /// <summary>
    /// Server-simulated straight-line projectile with pierce + optional explosion.
    /// Same authority model as AreaOfEffectNetworked: logic ONLY on the server,
    /// clients see the NetworkTransform-replicated visual.
    ///
    /// PREFAB: this + NetworkObject + NetworkTransform (server-auth) + child visual.
    /// No collider needed. Register in the Network Prefabs list.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class AbilityProjectile : NetworkBehaviour
    {
        [Min(0.05f)] [SerializeField] private float hitRadius = 0.35f;
        [Min(0.5f)] [SerializeField] private float maxLifetime = 6f;

        private static readonly Collider[] Buffer = new Collider[16];

        private ComposedAbilitySO _ability;
        private ProjectileDeliverySO _settings;
        private GameObject _caster;
        private Vector3 _direction;
        private float _traveled;
        private float _lifetime;
        private int _hitsLeft;
        private readonly HashSet<GameObject> _alreadyHit = new();

        /// <summary>Server-only. Call BEFORE NetworkObject.Spawn().</summary>
        public void ServerConfigure(ComposedAbilitySO ability, ProjectileDeliverySO settings,
            GameObject caster, Vector3 direction)
        {
            _ability = ability;
            _settings = settings;
            _caster = caster;
            _direction = direction.normalized;
            _hitsLeft = settings.pierceCount;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) enabled = false;
        }

        private void Update()
        {
            if (_settings == null) return;

            float step = _settings.speed * Time.deltaTime;
            transform.position += _direction * step;
            _traveled += step;
            _lifetime += Time.deltaTime;

            CheckHits();

            if (_settings != null && (_traveled >= _settings.maxRange || _lifetime >= maxLifetime))
            {
                Detonate(); // explosion at end-of-range (0 radius = nothing) + despawn
            }
        }

        private void CheckHits()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, hitRadius, Buffer, _settings.hitLayers, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                GameObject root = AbilityTargeting.ResolveRoot(Buffer[i]);
                if (root == null || root == _caster || _alreadyHit.Contains(root)) continue;
                if (!AbilityTargeting.PassesFilter(root, _caster, _ability.teamFilter)) continue;

                _alreadyHit.Add(root);
                AbilityTargeting.ApplyEffects(
                    _ability, root, _caster, Buffer[i].ClosestPoint(transform.position), _caster.transform.position);

                if (--_hitsLeft <= 0)
                {
                    Detonate();
                    return;
                }
            }
        }

        /// <summary>Optional AoE (explosionRadius) around the current position, then despawn.</summary>
        private void Detonate()
        {
            if (_settings != null && _settings.explosionRadius > 0f)
            {
                int count = Physics.OverlapSphereNonAlloc(
                    transform.position, _settings.explosionRadius, Buffer,
                    _settings.hitLayers, QueryTriggerInteraction.Collide);

                for (int i = 0; i < count; i++)
                {
                    GameObject root = AbilityTargeting.ResolveRoot(Buffer[i]);
                    if (root == null || root == _caster || _alreadyHit.Contains(root)) continue;
                    if (!AbilityTargeting.PassesFilter(root, _caster, _ability.teamFilter)) continue;

                    _alreadyHit.Add(root);
                    AbilityTargeting.ApplyEffects(
                        _ability, root, _caster, root.transform.position, transform.position);
                }
                // Hook: explosion VFX ClientRpc here.
            }

            _settings = null; // stop simulating even if despawn is deferred a frame
            if (NetworkObject.IsSpawned) NetworkObject.Despawn(true);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, hitRadius);
        }
#endif
    }
}
