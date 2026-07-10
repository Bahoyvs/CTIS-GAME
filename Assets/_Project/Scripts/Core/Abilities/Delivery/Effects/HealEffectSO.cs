using CBuilding.Heroes;
using UnityEngine;

namespace CBuilding.Abilities.Delivery
{
    /// <summary>
    /// Instant heal (goes through BaseHero.ServerHeal → GS-5.4 anti-heal pipeline).
    /// Gobluna S1 ally side, Ok S2 tether heal. Default appliesTo = AlliesAndSelf.
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
                hero.ServerHeal(healAmount);
            }
        }
    }
}
