using UnityEngine;

namespace CBuilding.Abilities.Delivery
{
    /// <summary>
    /// Melee cone/crescent toward the aim point. Kerem's Feature (wide crescent AoE swing).
    /// </summary>
    [CreateAssetMenu(menuName = "CBuilding/Abilities/Deliveries/Arc (Cone)", fileName = "Del_Arc")]
    public class ArcDeliverySO : AbilityDeliverySO
    {
        [Min(0.1f)] public float range = 3f;
        [Tooltip("Total arc width in degrees (140 = wide crescent).")]
        [Range(10f, 360f)] public float arcAngle = 140f;
        public LayerMask hitLayers = ~0;

        public override void Execute(in AbilityCastContext ctx)
        {
            Vector3 aimDir = ctx.AimPoint - ctx.Origin;
            aimDir.y = 0f;
            aimDir = aimDir.sqrMagnitude > 0.01f ? aimDir.normalized : ctx.Caster.transform.forward;

            int count = Physics.OverlapSphereNonAlloc(
                ctx.Origin, range, Buffer, hitLayers, QueryTriggerInteraction.Collide);

            // Angle-filter in place, then funnel through the shared apply path.
            float halfAngleCos = Mathf.Cos(arcAngle * 0.5f * Mathf.Deg2Rad);
            int kept = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 to = Buffer[i].transform.position - ctx.Origin;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f && Vector3.Dot(to.normalized, aimDir) < halfAngleCos) continue;
                Buffer[kept++] = Buffer[i];
            }

            ApplyToOverlaps(in ctx, kept, ctx.Origin);
        }
    }
}
