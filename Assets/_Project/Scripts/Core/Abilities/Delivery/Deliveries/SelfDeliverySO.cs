using UnityEngine;

namespace CBuilding.Abilities.Delivery
{
    /// <summary>Targets only the caster. Ironworks Feature (instant shield), Bahadır Feature (invis+MS).</summary>
    [CreateAssetMenu(menuName = "CBuilding/Abilities/Deliveries/Self", fileName = "Del_Self")]
    public class SelfDeliverySO : AbilityDeliverySO
    {
        public override void Execute(in AbilityCastContext ctx)
        {
            AbilityTargeting.ApplyEffects(ctx.Ability, ctx.Caster, ctx.Caster, ctx.Origin, ctx.Origin);
        }
    }
}
