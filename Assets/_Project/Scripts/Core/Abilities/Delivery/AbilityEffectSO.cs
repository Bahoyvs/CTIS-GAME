using UnityEngine;

namespace CBuilding.Abilities.Delivery
{
    /// <summary>
    /// One thing that lands on one target (damage, heal, status, displacement...).
    /// A ComposedAbilitySO carries a LIST of these — composition is what lets one
    /// delivery serve dual payloads (Gobluna S1: DamageEffect hits enemies while
    /// HealEffect hits allies, both from the same piercing arrows).
    ///
    /// Apply() runs on the SERVER only. Each effect self-filters via appliesTo,
    /// so the delivery can acquire a mixed crowd and effects sort it out.
    /// </summary>
    public abstract class AbilityEffectSO : ScriptableObject
    {
        [Tooltip("Which side of the acquired targets this effect actually lands on.")]
        public TeamFilter appliesTo = TeamFilter.Enemies;

        public void Apply(in EffectContext ctx)
        {
            if (!AbilityTargeting.PassesFilter(ctx.Target, ctx.Caster, appliesTo)) return;
            OnApply(in ctx);
        }

        protected abstract void OnApply(in EffectContext ctx);
    }
}
