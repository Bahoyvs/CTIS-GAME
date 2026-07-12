using System.Collections.Generic;
using CBuilding.Abilities;
using CBuilding.Abilities.Delivery;
using CBuilding.Core;
using CBuilding.Data;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Heroes
{
    /// <summary>
    /// GS-17 §6.4 — AP's evolving basic attack.
    ///
    /// PRIMARY SHOT: one projectile ability (longer range than the generic default —
    /// authored on the SO). Grants 1 Royalty Point; every 10 points permanently speeds
    /// up his attacks, UNCAPPED — implemented as a single recomputed StatModifier on
    /// AttackCooldown. Zero new cooldown architecture: HeroController already re-reads
    /// Stats on every trigger (§2), which is exactly why this works.
    ///
    /// CHAIN SHOTS (by Section, from SectionManager): S1 none; S2 +2 closest to the
    /// target; S3 +2 closest AND +3 farthest-behind-target (fallback: redirected to the
    /// closest remaining if too few behind). One internal call path with a bool —
    /// FireShot(..., countsForResource) — so Royalty gain and any future
    /// on-basic-attack-only trigger can't drift out of sync (§6.4).
    /// Rec #5: chain shots use a RAW-DAMAGE-ONLY ability asset (no on-hit procs) —
    /// consistent with them not granting Royalty Points; avoids the uncapped-attack-
    /// speed power spiral.
    ///
    /// ULTIMATE MODE: the button is repurposed entirely — hold channels a beam.
    /// Gated by hero.IsInUltimateMode (the mode switch decides, not tap/hold timing).
    /// </summary>
    public class APBasicAttackController : NetworkBehaviour, IBasicAttackBehaviour, IHoldableBasicAttack
    {
        [Header("Shot abilities (ComposedAbilitySO, Projectile delivery, count 1)")]
        [Tooltip("Full effect list: damage + on-hit procs. Longer maxRange than the generic default.")]
        [SerializeField] private ComposedAbilitySO primaryShotAbility;
        [Tooltip("Rec #5: RAW DAMAGE ONLY — a stripped asset whose effect list is just DamageEffect. No lifesteal/label procs/marks.")]
        [SerializeField] private ComposedAbilitySO chainShotAbility;

        [Header("Chain targeting")]
        [Tooltip("Search radius around the aim point for the primary target and its chain neighbours.")]
        [SerializeField] private float chainSearchRadius = 7f;
        [SerializeField] private LayerMask enemyLayers = ~0;

        [Header("Royalty Points (server-authoritative)")]
        [Tooltip("Attack-speed step granted per full 10 points. 0.10 = +10% attack speed per tier, forever, uncapped.")]
        [SerializeField] private float attackSpeedPerTier = 0.10f;
        [SerializeField] private int pointsPerTier = 10;

        [Header("Ultimate Mode — beam channel")]
        [Tooltip("Line-delivery ability ticked repeatedly while the beam is held.")]
        [SerializeField] private ComposedAbilitySO beamTickAbility;
        [SerializeField] private float beamTickInterval = 0.2f;

        // Replicated so the HUD can show Royalty Points (GS-16).
        private readonly NetworkVariable<int> _netRoyaltyPoints = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<int> NetRoyaltyPoints => _netRoyaltyPoints;

        private AbilityController _abilities;
        private HeroController _hero;
        private int _appliedTiers;

        // Beam state (server).
        private bool _beamActive;
        private Vector3 _beamAimPoint;
        private float _nextBeamTick;

        // §4 compression pattern for the beam aim point.
        private const float BeamSendThreshold = 0.15f;
        private const float BeamSendInterval = 1f / 14f;
        private Vector3 _lastSentBeamPoint;
        private float _nextBeamSendTime;

        private static readonly Collider[] Buffer = new Collider[32];

        private void Awake()
        {
            _abilities = GetComponent<AbilityController>();
            _hero = GetComponent<HeroController>();
        }

        // ---- IBasicAttackBehaviour (SERVER) ----

        public void Fire(HeroController hero, Vector3 aimPoint)
        {
            if (hero.IsInUltimateMode) return; // ult mode: taps do nothing, hold = beam
            if (primaryShotAbility == null || _abilities == null) return;

            // THE basic attack — the only shot that counts for resources (§6.4).
            FireShot(primaryShotAbility, aimPoint, countsForResource: true);

            int section = SectionManager.CurrentSection;
            if (section < 2 || chainShotAbility == null) return;

            GameObject primary = FindClosestEnemy(aimPoint, exclude: null);
            if (primary == null) return;

            Vector3 aimDir = aimPoint - transform.position;
            aimDir.y = 0f;
            aimDir = aimDir.sqrMagnitude > 0.01f ? aimDir.normalized : transform.forward;

            var used = new List<GameObject> { primary };

            // S2+: +2 closest to the target.
            AddClosestTargets(primary.transform.position, 2, used);

            // S3: +3 farthest-behind-target, redirecting to closest remaining if too few.
            if (section >= 3)
                AddFarthestBehindTargets(primary.transform.position, aimDir, 3, used);

            // Chain shots: same internal path, flag off — no Royalty Points (§6.4),
            // raw-damage-only asset (rec #5).
            for (int i = 1; i < used.Count; i++)
                FireShot(chainShotAbility, used[i].transform.position, countsForResource: false);
        }

        /// <summary>§6.4 — the one call path. The bool is the ONLY difference between primary and chain shots.</summary>
        private void FireShot(ComposedAbilitySO ability, Vector3 towardPoint, bool countsForResource)
        {
            ability.ExecuteDelivery(_abilities, towardPoint);

            if (!countsForResource) return;

            _netRoyaltyPoints.Value++;
            int tiers = _netRoyaltyPoints.Value / pointsPerTier;
            if (tiers != _appliedTiers) ApplyAttackSpeedTiers(tiers);
        }

        /// <summary>
        /// Uncapped attack-speed scaling as ONE recomputed modifier: cooldown multiplier
        /// = 1 / (1 + bonus·tiers). Removing + re-adding via the source key keeps the
        /// stat sheet clean at any tier count.
        /// </summary>
        private void ApplyAttackSpeedTiers(int tiers)
        {
            _appliedTiers = tiers;
            var stats = _hero != null ? _hero.Stats : GetComponent<HeroStatController>();
            if (stats == null) return;

            stats.RemoveModifiersFromSource(this);
            if (tiers <= 0) return;

            float cooldownMultiplier = 1f / (1f + attackSpeedPerTier * tiers);
            stats.ApplyModifiers(new[]
            {
                new StatModifierDefinition
                {
                    Stat = StatType.AttackCooldown,
                    Type = StatModType.PercentMult,
                    Value = cooldownMultiplier - 1f, // PercentMult applies (1 + Value)
                }
            }, this);
        }

        // ---- Chain target queries (SERVER) ----

        private GameObject FindClosestEnemy(Vector3 point, GameObject exclude)
        {
            int count = Physics.OverlapSphereNonAlloc(point, chainSearchRadius, Buffer, enemyLayers,
                QueryTriggerInteraction.Collide);

            GameObject best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                GameObject root = AbilityTargeting.ResolveRoot(Buffer[i]);
                if (root == null || root == exclude || root == gameObject) continue;
                if (!AbilityTargeting.PassesFilter(root, gameObject, TeamFilter.Enemies)) continue;

                float sqr = (root.transform.position - point).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = root; }
            }
            return best;
        }

        private void AddClosestTargets(Vector3 origin, int wanted, List<GameObject> used)
        {
            int count = Physics.OverlapSphereNonAlloc(origin, chainSearchRadius, Buffer, enemyLayers,
                QueryTriggerInteraction.Collide);
            var candidates = CollectCandidates(count, used);

            candidates.Sort((a, b) =>
                (a.transform.position - origin).sqrMagnitude.CompareTo(
                (b.transform.position - origin).sqrMagnitude));

            for (int i = 0; i < candidates.Count && wanted > 0; i++, wanted--)
                used.Add(candidates[i]);
        }

        private void AddFarthestBehindTargets(Vector3 origin, Vector3 aimDir, int wanted, List<GameObject> used)
        {
            int count = Physics.OverlapSphereNonAlloc(origin, chainSearchRadius, Buffer, enemyLayers,
                QueryTriggerInteraction.Collide);
            var candidates = CollectCandidates(count, used);

            // "Behind the target" = further along the aim direction than the primary.
            var behind = new List<GameObject>();
            var rest = new List<GameObject>();
            foreach (var c in candidates)
            {
                if (Vector3.Dot(c.transform.position - origin, aimDir) > 0f) behind.Add(c);
                else rest.Add(c);
            }

            // Farthest-behind first.
            behind.Sort((a, b) =>
                Vector3.Dot(b.transform.position - origin, aimDir).CompareTo(
                Vector3.Dot(a.transform.position - origin, aimDir)));

            for (int i = 0; i < behind.Count && wanted > 0; i++, wanted--)
                used.Add(behind[i]);

            // Fallback redirection (§6.4): too few behind → closest remaining instead.
            if (wanted > 0)
            {
                rest.Sort((a, b) =>
                    (a.transform.position - origin).sqrMagnitude.CompareTo(
                    (b.transform.position - origin).sqrMagnitude));
                for (int i = 0; i < rest.Count && wanted > 0; i++, wanted--)
                    used.Add(rest[i]);
            }
        }

        private List<GameObject> CollectCandidates(int overlapCount, List<GameObject> used)
        {
            var result = new List<GameObject>();
            for (int i = 0; i < overlapCount; i++)
            {
                GameObject root = AbilityTargeting.ResolveRoot(Buffer[i]);
                if (root == null || root == gameObject || used.Contains(root) || result.Contains(root)) continue;
                if (!AbilityTargeting.PassesFilter(root, gameObject, TeamFilter.Enemies)) continue;
                result.Add(root);
            }
            return result;
        }

        // ---- IHoldableBasicAttack — ONLY live in Ultimate Mode (§6.4) ----

        public bool HoldEnabled(HeroController hero) => hero.IsInUltimateMode;

        public void OnHoldBegin(HeroController hero, Vector3 aimPoint)
        {
            _lastSentBeamPoint = aimPoint;
            _nextBeamSendTime = 0f;
            BeginBeamServerRpc(aimPoint);
        }

        public void OnHoldUpdate(HeroController hero, Vector3 currentWorldPoint)
        {
            if ((currentWorldPoint - _lastSentBeamPoint).sqrMagnitude < BeamSendThreshold * BeamSendThreshold) return;
            if (Time.time < _nextBeamSendTime) return;

            _lastSentBeamPoint = currentWorldPoint;
            _nextBeamSendTime = Time.time + BeamSendInterval;
            UpdateBeamAimServerRpc(currentWorldPoint);
        }

        public void OnHoldRelease(HeroController hero) => EndBeamServerRpc();

        [ServerRpc]
        private void BeginBeamServerRpc(Vector3 aimPoint)
        {
            if (_hero == null || !_hero.IsAlive || !_hero.IsInUltimateMode) return;
            _beamActive = true;
            _beamAimPoint = aimPoint;
            _nextBeamTick = 0f;
        }

        [ServerRpc]
        private void UpdateBeamAimServerRpc(Vector3 aimPoint)
        {
            if (_beamActive) _beamAimPoint = aimPoint;
        }

        [ServerRpc]
        private void EndBeamServerRpc() => _beamActive = false;

        private void Update()
        {
            if (!IsServer || !_beamActive) return;

            // Mode ended or died mid-channel → beam stops itself.
            if (_hero == null || !_hero.IsAlive || !_hero.IsInUltimateMode)
            {
                _beamActive = false;
                return;
            }

            if (Time.time < _nextBeamTick || beamTickAbility == null || _abilities == null) return;
            _nextBeamTick = Time.time + beamTickInterval;
            beamTickAbility.ExecuteDelivery(_abilities, _beamAimPoint);
        }
    }
}
