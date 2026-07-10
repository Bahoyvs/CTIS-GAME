using UnityEngine;

namespace CBuilding.Abilities.Delivery
{
    /// <summary>
    /// HOW an ability finds its targets (shape/vehicle), decoupled from WHAT lands on
    /// them (AbilityEffectSO list) and WHO is valid (TeamFilter). One delivery asset can
    /// be shared by many abilities ("MeleeArc_140deg_3m" serves any crescent swing).
    ///
    /// Execute() runs on the SERVER only, from ComposedAbilitySO's runtime.
    /// </summary>
    public abstract class AbilityDeliverySO : ScriptableObject
    {
        public abstract void Execute(in AbilityCastContext ctx);

        /// <summary>Shared scratch buffer for overlap queries (server-only, single-threaded).</summary>
        protected static readonly Collider[] Buffer = new Collider[32];

        /// <summary>Overlap-based deliveries funnel through this: resolve, filter, dedupe, apply.</summary>
        protected static void ApplyToOverlaps(in AbilityCastContext ctx, int count, Vector3 hitRefPoint)
        {
            // Dedupe multi-collider targets without allocation: linear scan of applied roots.
            int appliedCount = 0;
            var applied = ArrayPool ??= new GameObject[32];

            for (int i = 0; i < count; i++)
            {
                GameObject root = AbilityTargeting.ResolveRoot(Buffer[i]);
                if (root == null) continue;
                if (!AbilityTargeting.PassesFilter(root, ctx.Caster, ctx.Ability.teamFilter)) continue;

                bool seen = false;
                for (int j = 0; j < appliedCount; j++)
                {
                    if (applied[j] == root) { seen = true; break; }
                }
                if (seen) continue;
                if (appliedCount < applied.Length) applied[appliedCount++] = root;

                AbilityTargeting.ApplyEffects(
                    ctx.Ability, root, ctx.Caster, Buffer[i].ClosestPoint(hitRefPoint), ctx.Origin);
            }

            System.Array.Clear(applied, 0, appliedCount);
        }

        private static GameObject[] ArrayPool;
    }
}
