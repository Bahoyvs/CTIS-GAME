using UnityEngine;

namespace CBuilding.Abilities.Delivery
{
    /// <summary>
    /// Auto-targets the N closest valid targets around the caster — no aiming needed.
    /// TL Ult's ally-heal ticks (nearest allies), auto-zap style effects.
    /// </summary>
    [CreateAssetMenu(menuName = "CBuilding/Abilities/Deliveries/Nearest", fileName = "Del_Nearest")]
    public class NearestDeliverySO : AbilityDeliverySO
    {
        [Min(0.1f)] public float searchRange = 6f;
        [Min(1)] public int maxTargets = 1;
        public LayerMask hitLayers = ~0;

        public override void Execute(in AbilityCastContext ctx)
        {
            int count = Physics.OverlapSphereNonAlloc(
                ctx.Origin, searchRange, Buffer, hitLayers, QueryTriggerInteraction.Collide);

            // Selection sort of the closest maxTargets valid roots (count is small).
            for (int picked = 0; picked < maxTargets; picked++)
            {
                GameObject best = null;
                float bestSqr = float.MaxValue;
                int bestIdx = -1;

                for (int i = 0; i < count; i++)
                {
                    if (Buffer[i] == null) continue;
                    GameObject root = AbilityTargeting.ResolveRoot(Buffer[i]);
                    if (root == null ||
                        !AbilityTargeting.PassesFilter(root, ctx.Caster, ctx.Ability.teamFilter)) continue;

                    float sqr = (root.transform.position - ctx.Origin).sqrMagnitude;
                    if (sqr < bestSqr) { bestSqr = sqr; best = root; bestIdx = i; }
                }

                if (best == null) return;
                Buffer[bestIdx] = null; // consume so the next pass picks the next-closest
                AbilityTargeting.ApplyEffects(ctx.Ability, best, ctx.Caster, best.transform.position, ctx.Origin);
            }
        }
    }
}
