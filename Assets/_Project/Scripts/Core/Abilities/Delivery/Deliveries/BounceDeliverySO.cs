using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Abilities.Delivery
{
    /// <summary>
    /// GS-17 §7.2 — Bounce/Chain delivery (Bounce Orb, all three tiers; clears the
    /// GS-9.4 reusability bar on its own). Spawns one BounceProjectile that flies
    /// straight until first enemy contact, then chains between nearby targets.
    ///
    /// Rec #10 targeting rules (implemented in BounceProjectile):
    ///   - Enemies are ALWAYS prioritized while any remain in bounce radius; ally/self
    ///     only become valid when no enemy is left (never heals past a finishable kill).
    ///   - Tie-break: closest to the previous bounce point, then lowest HP% (rewards
    ///     finishing weak targets).
    ///   - Only the IMMEDIATELY PRIOR target is excluded, not full chain history —
    ///     keeps it bouncy rather than deterministic; the bounce cap limits abuse.
    ///
    /// SECTION TIERS: three assets — S1 (base): 2 bounces, enemies only. S2: 4
    /// bounces, adds allowSelfBounce (heals the caster). S3: 5 bounces, adds
    /// allowAllyBounce (heals allies) and allowWallBounce (caroms off walls
    /// instead of fizzling). The ability's effect list carries Damage(appliesTo
    /// Enemies) + Heal(appliesTo AlliesAndSelf) with teamFilter widening to All
    /// from S2 onward so both sides are acquirable.
    /// </summary>
    [CreateAssetMenu(menuName = "CBuilding/Abilities/Deliveries/Bounce", fileName = "Del_Bounce")]
    public class BounceDeliverySO : AbilityDeliverySO
    {
        [Header("Prefab (BounceProjectile + NetworkObject + NetworkTransform, in Network Prefabs list)")]
        public NetworkObject projectilePrefab;

        [Header("Flight")]
        [Min(0.1f)] public float speed = 12f;
        [Tooltip("Max straight-line distance before the orb fizzles without a first hit.")]
        [Min(0.5f)] public float maxRange = 10f;

        [Header("Chain")]
        [Tooltip("Total targets hit including the first (S1 = 2, S2 = 4, S3 = 5).")]
        [Min(1)] public int maxBounces = 2;
        [Tooltip("Search radius around the current target for the next bounce.")]
        [Min(0.5f)] public float bounceRadius = 5f;
        [Tooltip("Section 3: allies become valid bounce targets (heal) once no enemy remains in radius.")]
        public bool allowAllyBounce = false;
        [Tooltip("Section 2+: the caster themself becomes a valid fallback bounce target (heal).")]
        public bool allowSelfBounce = false;
        [Tooltip("Section 3: when no living target remains in bounceRadius, the orb may carom off a nearby wall and keep flying instead of fizzling. Wall caroms don't consume a bounce — only landed hits count toward maxBounces.")]
        public bool allowWallBounce = false;
        [Tooltip("Collider layers considered 'wall' for the Section 3 wall-bounce fallback.")]
        public LayerMask wallLayers = 0;

        [Header("Physics")]
        public LayerMask hitLayers = ~0;

        public override void Execute(in AbilityCastContext ctx)
        {
            if (projectilePrefab == null)
            {
                Debug.LogWarning($"[{name}] No projectilePrefab assigned.");
                return;
            }

            Vector3 dir = ctx.AimPoint - ctx.Origin;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : ctx.Caster.transform.forward;

            Vector3 spawnPos = ctx.Origin + dir * 0.6f + Vector3.up * 0.5f;
            NetworkObject instance = Object.Instantiate(
                projectilePrefab, spawnPos, Quaternion.LookRotation(dir));

            if (instance.TryGetComponent<BounceProjectile>(out var orb))
            {
                orb.ServerConfigure(ctx.Ability, this, ctx.Caster, dir); // BEFORE Spawn
            }
            instance.Spawn(true);
        }
    }
}
