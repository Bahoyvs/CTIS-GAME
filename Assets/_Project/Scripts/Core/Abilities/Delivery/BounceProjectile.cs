using CBuilding.Enemies;
using CBuilding.Heroes;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Abilities.Delivery
{
    /// <summary>
    /// GS-17 §7.2 — server-simulated bouncing orb (Bounce Orb archetype). Flies
    /// straight until the first ENEMY contact, then chains per the rec #10 rules
    /// documented on BounceDeliverySO. Same authority model as AbilityProjectile.
    ///
    /// PREFAB: this + NetworkObject + NetworkTransform (server-auth) + child visual.
    /// Register in the Network Prefabs list.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class BounceProjectile : NetworkBehaviour
    {
        [Min(0.05f)] [SerializeField] private float hitRadius = 0.4f;
        [Min(0.5f)] [SerializeField] private float maxLifetime = 8f;
        [Tooltip("Two next-bounce candidates within this distance difference count as 'equidistant' → lowest HP% wins.")]
        [Min(0f)] [SerializeField] private float tieBreakEpsilon = 0.25f;

        private static readonly Collider[] Buffer = new Collider[32];

        private ComposedAbilitySO _ability;
        private BounceDeliverySO _settings;
        private GameObject _caster;
        private Vector3 _direction;
        private float _traveled;
        private float _lifetime;
        private int _bouncesDone;

        private GameObject _seekTarget;    // null while in the initial straight flight
        private GameObject _previousTarget; // rec #10: the ONLY excluded candidate

        /// <summary>Server-only. Call BEFORE NetworkObject.Spawn().</summary>
        public void ServerConfigure(ComposedAbilitySO ability, BounceDeliverySO settings,
            GameObject caster, Vector3 direction)
        {
            _ability = ability;
            _settings = settings;
            _caster = caster;
            _direction = direction.normalized;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) enabled = false;
        }

        private void Update()
        {
            if (_settings == null) return;

            _lifetime += Time.deltaTime;
            if (_lifetime >= maxLifetime) { Despawn(); return; }

            if (_seekTarget == null) UpdateStraightFlight();
            else UpdateSeek();
        }

        // ---- Phase 1: straight flight, first contact must be an enemy ----

        private void UpdateStraightFlight()
        {
            float step = _settings.speed * Time.deltaTime;
            transform.position += _direction * step;
            _traveled += step;

            int count = Physics.OverlapSphereNonAlloc(
                transform.position, hitRadius, Buffer, _settings.hitLayers, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                GameObject root = AbilityTargeting.ResolveRoot(Buffer[i]);
                if (root == null || root == _caster) continue;
                // First contact: enemies only — an orb should never open by healing a
                // full-HP ally it happened to cross (rec #10 priority, applied early).
                if (!AbilityTargeting.PassesFilter(root, _caster, TeamFilter.Enemies)) continue;

                HitAndChain(root);
                return;
            }

            if (_traveled >= _settings.maxRange) Despawn();
        }

        // ---- Phase 2: seek the chosen bounce target ----

        private void UpdateSeek()
        {
            // Mid-flight death/despawn of the seek target: resolve at the LAST bounce
            // point (its final position) rather than re-validating continuously.
            if (_seekTarget == null || !_seekTarget.activeInHierarchy) { Despawn(); return; }

            Vector3 to = _seekTarget.transform.position + Vector3.up * 0.5f - transform.position;
            float step = _settings.speed * Time.deltaTime;

            if (to.magnitude <= Mathf.Max(step, hitRadius))
            {
                HitAndChain(_seekTarget);
                return;
            }

            _direction = to.normalized;
            transform.position += _direction * step;
        }

        // ---- Chain resolution ----

        private void HitAndChain(GameObject target)
        {
            // The ability's effect list self-filters per side: Damage(appliesTo Enemies)
            // hits enemies, Heal(appliesTo AlliesAndSelf) covers S2/S3 ally bounces —
            // one code path, the same rule Gobluna S1 already uses.
            AbilityTargeting.ApplyEffects(_ability, target, _caster,
                target.transform.position, transform.position);

            _bouncesDone++;
            if (_bouncesDone >= _settings.maxBounces) { Despawn(); return; }

            GameObject next = PickNextTarget(target);
            if (next == null) { Despawn(); return; }

            _previousTarget = target;
            _seekTarget = next;
        }

        /// <summary>Rec #10 in full: enemy priority → closest-to-previous-bounce-point → lowest HP%; excludes only the immediately prior target.</summary>
        private GameObject PickNextTarget(GameObject from)
        {
            Vector3 origin = from.transform.position;
            int count = Physics.OverlapSphereNonAlloc(
                origin, _settings.bounceRadius, Buffer, _settings.hitLayers, QueryTriggerInteraction.Collide);

            GameObject bestEnemy = null, bestAlly = null;
            float bestEnemyDist = float.MaxValue, bestAllyDist = float.MaxValue;
            float bestEnemyHp = float.MaxValue, bestAllyHp = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                GameObject root = AbilityTargeting.ResolveRoot(Buffer[i]);
                if (root == null || root == from || root == _previousTarget) continue;

                bool isEnemy = AbilityTargeting.PassesFilter(root, _caster, TeamFilter.Enemies);
                if (!isEnemy)
                {
                    bool allyOk = _settings.allowAllyBounce &&
                                  AbilityTargeting.PassesFilter(root, _caster, TeamFilter.Allies);
                    bool selfOk = _settings.allowSelfBounce && root == _caster;
                    if (!allyOk && !selfOk) continue;
                }

                float dist = Vector3.Distance(origin, root.transform.position);
                float hp = HealthFraction(root);

                if (isEnemy) Consider(root, dist, hp, ref bestEnemy, ref bestEnemyDist, ref bestEnemyHp);
                else Consider(root, dist, hp, ref bestAlly, ref bestAllyDist, ref bestAllyHp);
            }

            // Enemies always win while any remain in radius; ally/self are the fallback.
            return bestEnemy != null ? bestEnemy : bestAlly;
        }

        private void Consider(GameObject candidate, float dist, float hp,
            ref GameObject best, ref float bestDist, ref float bestHp)
        {
            bool better;
            if (best == null) better = true;
            else if (Mathf.Abs(dist - bestDist) <= tieBreakEpsilon) better = hp < bestHp; // equidistant → lowest HP%
            else better = dist < bestDist;

            if (!better) return;
            best = candidate;
            bestDist = dist;
            bestHp = hp;
        }

        private static float HealthFraction(GameObject root)
        {
            if (root.TryGetComponent<BaseHero>(out var hero))
            {
                float max = hero.Stats != null ? hero.Stats.GetStat(CBuilding.Data.StatType.MaxHealth) : 1f;
                return max > 0f ? hero.CurrentHealth / max : 1f;
            }
            if (root.TryGetComponent<BaseEnemy>(out var enemy))
            {
                return enemy.MaxHealth > 0f ? enemy.NetHealth.Value / enemy.MaxHealth : 1f;
            }
            return 1f;
        }

        private void Despawn()
        {
            _settings = null;
            if (NetworkObject.IsSpawned) NetworkObject.Despawn(true);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, hitRadius);
        }
#endif
    }
}
