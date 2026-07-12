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
    /// SECTION TIERS: three assets — S1: allowAlly/Self off; S2/S3: on, with the
    /// ability's effect list carrying Damage(appliesTo Enemies) + Heal(appliesTo
    /// AlliesAndSelf) and teamFilter = All so both sides are acquirable.
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
        [Tooltip("Total targets hit including the first (Bounce Orb cap = 5).")]
        [Min(1)] public int maxBounces = 3;
        [Tooltip("Search radius around the current target for the next bounce.")]
        [Min(0.5f)] public float bounceRadius = 5f;
        [Tooltip("Section 2+: allies become valid bounce targets (heal) once no enemy remains in radius.")]
        public bool allowAllyBounce = false;
        [Tooltip("Section 3: the caster themself becomes a valid fallback bounce target.")]
        public bool allowSelfBounce = false;

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
