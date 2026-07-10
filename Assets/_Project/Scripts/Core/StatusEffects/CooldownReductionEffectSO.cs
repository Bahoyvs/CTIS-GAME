using CBuilding.Abilities;
using UnityEngine;

namespace CBuilding.StatusEffects
{
    /// <summary>
    /// GS-9 §2 — "Instant Status" cooldown reduction. Rather than adding a new Effects
    /// primitive (Damage/Heal/ApplyStatus/Displacement have no "reduce CD" concept), this
    /// rides the existing ApplyStatus pipeline as a zero-duration status whose OnApply hook
    /// calls the shared <see cref="CooldownManager.ReduceAllActive"/> API.
    ///
    /// Hero-agnostic and shared on purpose (see plan doc §2): any future kit needing the
    /// same "instant CD pulse" pattern reuses this asset/class instead of duplicating it.
    /// First consumers: Bahadır's Final Passive and Skill2's virus-return.
    ///
    /// Stacking note (composed-system test checklist): duration is forced to 0, so two
    /// applications in the SAME server frame collapse into one OnApply call (StackingPolicy
    /// = Refresh matches the still-active zero-duration instance and just resets its
    /// Remaining, which is already ~0) — applications on different frames each fire fully.
    /// This is the intended "instant, non-double-dipping-within-a-frame" behaviour.
    /// </summary>
    [CreateAssetMenu(menuName = "CBuilding/Status Effects/Cooldown Reduction (Instant)", fileName = "Fx_CooldownReduction")]
    public class CooldownReductionEffectSO : EffectDataSO
    {
        [Header("Instant CD pulse")]
        [Tooltip("Seconds shaved off every currently-running cooldown on the target (float.MaxValue = full reset).")]
        [Min(0f)] public float reductionSeconds = 2f;

        public override IStatusEffect CreateRuntime() => new Runtime(this);

        private void Reset()
        {
            duration = 0f;
            tickInterval = 0f;
            stackingPolicy = StackingPolicy.Refresh;
            maxStacks = 1;
            isDebuff = false;
        }

        // Unity invokes OnValidate per class level (not standard virtual dispatch), so a
        // private method here runs IN ADDITION to EffectDataSO's own OnValidate — no
        // override keyword needed (and none is available; the base method is private).
        private void OnValidate()
        {
            // Instant by definition — a non-zero duration would just delay cleanup for no benefit.
            duration = 0f;
        }

        private class Runtime : IStatusEffect
        {
            public EffectDataSO Data { get; }
            private readonly CooldownReductionEffectSO _config;

            public Runtime(CooldownReductionEffectSO config)
            {
                _config = config;
                Data = config;
            }

            public void OnApply(StatusEffectContext context)
            {
                if (context.Target.TryGetComponent<AbilityController>(out var abilities))
                {
                    abilities.Cooldowns.ReduceAllActive(_config.reductionSeconds);
                }
            }

            public void OnTick(StatusEffectContext context, float deltaTime) { }
            public void OnExpire(StatusEffectContext context) { }
            public void OnStacksChanged(StatusEffectContext context, int stacks) { }
        }
    }
}
