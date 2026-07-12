using CBuilding.Core;
using CBuilding.Heroes;
using UnityEngine;

namespace CBuilding.Abilities.Delivery
{
    /// <summary>
    /// Instant heal (goes through BaseHero.ServerHeal → GS-5.4 anti-heal pipeline).
    /// Gobluna S1 ally side, Ok S2 tether heal. Default appliesTo = AlliesAndSelf.
    /// Real (post-clamp) healing is announced on TeamEventBus.OnAllyHealedAlly so
    /// heal-reactive kits (Gobluna S2 resource) hook the pipeline, not each asset.
    /// </summary>
    [CreateAssetMenu(menuName = "CBuilding/Abilities/Effects/Heal", fileName = "Fx_Heal")]
    public class HealEffectSO : AbilityEffectSO
    {
        [Min(0f)] public float healAmount = 10f;

        private void Reset() => appliesTo = TeamFilter.AlliesAndSelf;

        protected override void OnApply(in EffectContext ctx)
        {
            if (ctx.Target.TryGetComponent<BaseHero>(out var hero))
            {
                float healed = hero.ServerHeal(healAmount);
                if (healed > 0f)
                {
                    TeamEventBus.RaiseAllyHealedAlly(ctx.Caster, ctx.Target, healed);
                }
            }
        }
    }
}
