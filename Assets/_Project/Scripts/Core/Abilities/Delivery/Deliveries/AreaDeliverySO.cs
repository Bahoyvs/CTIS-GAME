using UnityEngine;

namespace CBuilding.Abilities.Delivery
{
    /// <summary>
    /// Instant circle at the (range-clamped) aim point — or centered on the caster when
    /// castRange = 0. TL S2's burst, Ok's passive pulse, Kerem Ult's final explosion.
    /// </summary>
    [CreateAssetMenu(menuName = "CBuilding/Abilities/Deliveries/Point Area", fileName = "Del_Area")]
    public class AreaDeliverySO : AbilityDeliverySO
    {
        [Tooltip("Max cast distance; the aim point is clamped into this. 0 = always centered on caster.")]
        [Min(0f)] public float castRange = 6f;
        [Min(0.1f)] public float radius = 2.5f;
        public LayerMask hitLayers = ~0;

        public override void Execute(in AbilityCastContext ctx)
        {
            Vector3 toAim = ctx.AimPoint - ctx.Origin;
            toAim.y = 0f;
            Vector3 center = ctx.Origin + Vector3.ClampMagnitude(toAim, castRange);

            int count = Physics.OverlapSphereNonAlloc(
                center, radius, Buffer, hitLayers, QueryTriggerInteraction.Collide);

            ApplyToOverlaps(in ctx, count, center);
        }
    }
}
