using CBuilding.Core;
using UnityEngine;

namespace CBuilding.StatusEffects
{
    /// <summary>
    /// Default data-driven runtime for EffectDataSO. Covers the whole GS-5.3 catalog
    /// without per-effect code: control flags, slow, DoT, and pipeline multipliers.
    /// Registers itself as an IDamageModifier when the data defines damage/heal
    /// multipliers (SpywareMark, Mark of Guilt, Anti-heal, Sunburn — GS-5.4).
    /// </summary>
    public class GenericStatusEffect : IStatusEffect, IDamageModifier
    {
        public EffectDataSO Data { get; }

        private int _stacks = 1;
        private DamageModifierPipeline _pipeline;
        private GameObject _source;

        public GenericStatusEffect(EffectDataSO data)
        {
            Data = data;
        }

        // ---- IStatusEffect ----

        public virtual void OnApply(StatusEffectContext context)
        {
            _source = context.Source;

            if (HasPipelineHook)
            {
                _pipeline = context.Target.GetComponent<DamageModifierPipeline>();
                _pipeline?.Register(this);
            }
        }

        public virtual void OnTick(StatusEffectContext context, float deltaTime)
        {
            if (Data.damagePerTick <= 0f) return;

            if (context.Target.TryGetComponent<IDamageable>(out var damageable))
            {
                // StackDuration DoTs keep base tick damage; StackIntensity DoTs scale with stacks.
                float amount = Data.stackingPolicy == StackingPolicy.StackIntensity
                    ? Data.damagePerTick * _stacks
                    : Data.damagePerTick;

                // DoT ticks: no knockback, no hit point of interest — flag as DoT so
                // hitstun/knockback reactions can ignore them later if needed.
                damageable.TakeDamage(new DamageInfo(
                    amount, context.Target.transform.position, Vector3.zero, 0f,
                    _source, DamageFlags.DoT));
            }
        }

        public virtual void OnExpire(StatusEffectContext context)
        {
            _pipeline?.Unregister(this);
            _pipeline = null;
        }

        public virtual void OnStacksChanged(StatusEffectContext context, int stacks)
        {
            _stacks = Mathf.Max(1, stacks);
        }

        // ---- IDamageModifier (GS-5.4) ----

        /// <summary>Multiplicative band (see IDamageModifier priority convention). Virtual so
        /// bespoke subclasses (e.g. Bahadır's melee-invincibility) can move into the 200+
        /// "clamps" band instead of the default multiplicative one.</summary>
        public virtual int Priority => 100;

        public virtual float Modify(in DamageInfo info, float currentAmount)
        {
            float multiplier = info.IsHealing ? Data.incomingHealMultiplier : Data.incomingDamageMultiplier;
            if (Mathf.Approximately(multiplier, 1f)) return currentAmount;

            // StackIntensity compounds the multiplier per stack.
            if (Data.stackingPolicy == StackingPolicy.StackIntensity && _stacks > 1)
            {
                multiplier = Mathf.Pow(multiplier, _stacks);
            }

            return currentAmount * multiplier;
        }

        private bool HasPipelineHook =>
            !Mathf.Approximately(Data.incomingDamageMultiplier, 1f) ||
            !Mathf.Approximately(Data.incomingHealMultiplier, 1f);
    }
}
