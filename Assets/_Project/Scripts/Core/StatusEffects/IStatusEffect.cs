using UnityEngine;

namespace CBuilding.StatusEffects
{
    /// <summary>
    /// GS-5.1 — runtime contract for a status effect instance.
    /// One instance is created per application via EffectDataSO.CreateRuntime().
    /// All hooks run on the SERVER only.
    /// </summary>
    public interface IStatusEffect
    {
        EffectDataSO Data { get; }

        void OnApply(StatusEffectContext context);

        /// <summary>Called every data.tickInterval seconds while active.</summary>
        void OnTick(StatusEffectContext context, float deltaTime);

        void OnExpire(StatusEffectContext context);

        /// <summary>Called when stacks change (StackIntensity policy).</summary>
        void OnStacksChanged(StatusEffectContext context, int stacks);
    }

    /// <summary>Everything an effect needs about its target and source.</summary>
    public readonly struct StatusEffectContext
    {
        public readonly StatusEffectController Controller;
        public readonly GameObject Target;
        public readonly GameObject Source;

        public StatusEffectContext(StatusEffectController controller, GameObject source)
        {
            Controller = controller;
            Target = controller.gameObject;
            Source = source;
        }
    }
}
