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
        private bool _retargeted; // GS-17 rec #12 — one snap per projectile, not continuous homing
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

            // GS-17 §7.2 canPierceWalls — Pierce Bolt S3 flies through terrain; everyone
            // else detonates on the wall they were about to cross this frame.
            if (!_settings.canPierceWalls && _settings.wallLayers != 0 &&
                Physics.Raycast(transform.position, _direction, out RaycastHit wallHit, step + hitRadius,
                    _settings.wallLayers, QueryTriggerInteraction.Ignore))
            {
                transform.position = wallHit.point - _direction * hitRadius * 0.5f;
                Detonate();
                return;
            }

            transform.position += _direction * step;
            _traveled += step;
            _lifetime += Time.deltaTime;

            TryApproachRetarget();
            CheckHits();

            if (_settings != null && (_traveled >= _settings.maxRange || _lifetime >= maxLifetime))
            {
                Detonate(); // explosion at end-of-range (0 radius = nothing) + despawn
            }
        }

        /// <summary>
        /// GS-17 rec #12 — cone-limited retarget-on-approach (Rapid Needle S3), NOT true
        /// homing. Only in the last retargetWindowFraction of the flight, only once, only
        /// toward a valid enemy within a tight cone/radius: forgives a near-miss without
        /// deleting the aim skill the weapon rig is built around.
        /// </summary>
        private void TryApproachRetarget()
        {
            if (_retargeted || !_settings.retargetOnApproach) return;
            if (_traveled < _settings.maxRange * (1f - _settings.retargetWindowFraction)) return;

            int count = Physics.OverlapSphereNonAlloc(
                transform.position, _settings.retargetRadius, Buffer, _settings.hitLayers,
                QueryTriggerInteraction.Collide);

            GameObject best = null;
            float bestAngle = _settings.retargetConeDeg;
            Vector3 bestPos = Vector3.zero;

            for (int i = 0; i < count; i++)
            {
                GameObject root = AbilityTargeting.ResolveRoot(Buffer[i]);
                if (root == null || root == _caster || _alreadyHit.Contains(root)) continue;
                if (!AbilityTargeting.PassesFilter(root, _caster, _ability.teamFilter)) continue;

                Vector3 to = root.transform.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude < 0.01f) continue;

                float angle = Vector3.Angle(_direction, to);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    best = root;
                    bestPos = to;
                }
            }

            if (best != null)
            {
                _retargeted = true;
                _direction = bestPos.normalized;
                transform.rotation = Quaternion.LookRotation(_direction);
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

                // Only HOSTILE hits consume pierce. With a mixed teamFilter (Gobluna S1:
                // EnemiesAndAllies) an ally crossing the dart's path is healed in passing —
                // it must not shorten the dart. Enemies-only abilities are unaffected
                // (allies never reach this line for them).
                if (AbilityTargeting.PassesFilter(root, _caster, TeamFilter.Enemies) && --_hitsLeft <= 0)
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
