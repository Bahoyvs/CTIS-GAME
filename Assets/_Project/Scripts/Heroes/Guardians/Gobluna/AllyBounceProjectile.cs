using System.Collections.Generic;
using CBuilding.Abilities.Delivery;
using CBuilding.Core;
using CBuilding.Heroes;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Heroes.Gobluna
{
    /// <summary>
    /// GS-9 — Gobluna Ultimate's "Bouncing Blessing" blast. The ALLY-side mirror of
    /// BounceProjectile: it seeks HEROES and bounces ally→ally for its whole lifetime
    /// (no bounce cap — the clock is the cap), healing each ally it reaches; ENEMIES
    /// caught along the travel path take damage as it passes through them.
    ///
    /// Why not reuse BounceProjectile: its chain rules are enemy-first by design
    /// (rec #10 — "never heals past a finishable kill") and it despawns after
    /// maxBounces. This blast inverts both: allies are the CHAIN, enemies are
    /// incidental collateral, and it lives exactly as long as the Ult mode says.
    ///
    /// Same authority model as every GS-17 projectile: server simulates, clients see
    /// the NetworkTransform-replicated visual.
    ///
    /// PREFAB: this + NetworkObject + NetworkTransform (server-auth) + child visual.
    /// Register in the Network Prefabs list. Damage numbers live HERE (serialized)
    /// because the blast is spawned by GoblunaHeroController, not by a delivery SO.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class AllyBounceProjectile : NetworkBehaviour
    {
        [Header("Flight")]
        [Tooltip("Kit spec: a SLOW projectile — enemies dodge it, allies walk to meet it.")]
        [Min(0.1f)] [SerializeField] private float speed = 5f;
        [Min(0.05f)] [SerializeField] private float hitRadius = 0.6f;
        [Tooltip("Max distance from the CURRENT ally to the next bounce candidate. No living ally in range = the blessing fizzles (despawn).")]
        [Min(0.5f)] [SerializeField] private float allySearchRadius = 14f;

        [Header("Payload")]
        [Tooltip("HP restored to each ally the blast reaches (per arrival, so a long life = many heals).")]
        [Min(0f)] [SerializeField] private float healPerArrival = 30f;
        [Tooltip("Damage to each enemy the blast passes through (once per travel leg).")]
        [Min(0f)] [SerializeField] private float damagePerPass = 15f;
        [SerializeField] private LayerMask enemyHitLayers = ~0;

        private static readonly Collider[] Buffer = new Collider[16];

        private GameObject _caster;
        private float _lifeRemaining;
        private BaseHero _seekTarget;
        private BaseHero _previousTarget; // mirror of BounceProjectile: only the immediately prior ally is excluded
        private readonly HashSet<GameObject> _hitThisLeg = new();

        /// <summary>Server-only. Call BEFORE NetworkObject.Spawn().</summary>
        public void ServerConfigure(GameObject caster, float lifetime)
        {
            _caster = caster;
            _lifeRemaining = lifetime;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
            {
                enabled = false;
                return;
            }

            // No-ally case (shouldn't happen — the caster is a valid target) is handled by
            // the first Update: despawning INSIDE the spawn callback is fragile in NGO.
            _seekTarget = PickNextAlly(transform.position);
        }

        private void Update()
        {
            if (_caster == null) return;

            _lifeRemaining -= Time.deltaTime;
            if (_lifeRemaining <= 0f) { Despawn(); return; }

            // Ally died / despawned mid-flight: re-acquire rather than fizzle — an 18s
            // ultimate shouldn't end because one teammate got unlucky.
            if (_seekTarget == null || !_seekTarget.IsAlive)
            {
                _seekTarget = PickNextAlly(transform.position);
                if (_seekTarget == null) { Despawn(); return; }
            }

            Vector3 to = _seekTarget.transform.position + Vector3.up * 0.5f - transform.position;
            float step = speed * Time.deltaTime;

            if (to.magnitude <= Mathf.Max(step, hitRadius))
            {
                ArriveAt(_seekTarget);
                return;
            }

            transform.position += to.normalized * step;
            DamageEnemiesOnPath();
        }

        // ---- Travel-path collateral ----

        private void DamageEnemiesOnPath()
        {
            if (damagePerPass <= 0f) return;

            int count = Physics.OverlapSphereNonAlloc(
                transform.position, hitRadius, Buffer, enemyHitLayers, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                GameObject root = AbilityTargeting.ResolveRoot(Buffer[i]);
                if (root == null || root == _caster || _hitThisLeg.Contains(root)) continue;
                if (!AbilityTargeting.PassesFilter(root, _caster, TeamFilter.Enemies)) continue;

                _hitThisLeg.Add(root);

                if (root.TryGetComponent<IDamageable>(out var damageable))
                {
                    // Instigator = Gobluna → this damage feeds her Siphoner passive and
                    // Green Fire mark through the SAME TeamEventBus path as everything else.
                    damageable.TakeDamage(new DamageInfo(
                        damagePerPass, Buffer[i].ClosestPoint(transform.position),
                        Vector3.zero, 0f, _caster, DamageFlags.Ability));
                }
            }
        }

        // ---- Chain ----

        private void ArriveAt(BaseHero ally)
        {
            float healed = ally.ServerHeal(healPerArrival);
            if (healed > 0f && _caster != null)
            {
                // Ult heals fill the Skill2 resource bar too — same pipeline as HealEffectSO.
                TeamEventBus.RaiseAllyHealedAlly(_caster, ally.gameObject, healed);
            }

            _previousTarget = ally;
            _seekTarget = PickNextAlly(ally.transform.position);
            _hitThisLeg.Clear(); // new leg: enemies straddling the bounce point can be hit again

            if (_seekTarget == null) Despawn();
        }

        /// <summary>
        /// Nearest living hero (INCLUDING the caster — solo/duo runs need a valid ping-pong
        /// partner) within allySearchRadius, excluding only the immediately prior target.
        /// Registry scan, not physics: heroes are few and never miss a layer mask.
        /// </summary>
        private BaseHero PickNextAlly(Vector3 from)
        {
            BaseHero best = null;
            float bestSqr = allySearchRadius * allySearchRadius;

            for (int i = 0; i < BaseHero.ActiveHeroes.Count; i++)
            {
                BaseHero hero = BaseHero.ActiveHeroes[i];
                if (hero == null || !hero.IsAlive || hero == _previousTarget) continue;

                float sqr = (hero.transform.position - from).sqrMagnitude;
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    best = hero;
                }
            }
            return best;
        }

        private void Despawn()
        {
            _caster = null; // stop simulating even if despawn is deferred a frame
            if (NetworkObject.IsSpawned) NetworkObject.Despawn(true);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, hitRadius);
        }
#endif
    }
}
