using UnityEngine;

namespace CBuilding.Abilities.Delivery
{
    /// <summary>
    /// Oriented box from the caster toward the aim point. Kerem S2 (pull enemies into a
    /// line), AP Ult laser sweeps (cast repeatedly per tick from a channel runtime).
    /// </summary>
    [CreateAssetMenu(menuName = "CBuilding/Abilities/Deliveries/Line", fileName = "Del_Line")]
    public class LineDeliverySO : AbilityDeliverySO
    {
        [Min(0.5f)] public float length = 8f;
        [Min(0.1f)] public float width = 2f;
        [Min(0.5f)] public float height = 2f;
        public LayerMask hitLayers = ~0;

        public override void Execute(in AbilityCastContext ctx)
        {
            Vector3 dir = ctx.AimPoint - ctx.Origin;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : ctx.Caster.transform.forward;

            Vector3 center = ctx.Origin + dir * (length * 0.5f);
            var halfExtents = new Vector3(width * 0.5f, height * 0.5f, length * 0.5f);
            Quaternion rot = Quaternion.LookRotation(dir);

            int count = Physics.OverlapBoxNonAlloc(
                center, halfExtents, Buffer, rot, hitLayers, QueryTriggerInteraction.Collide);

            ApplyToOverlaps(in ctx, count, ctx.Origin);
        }
    }
}
