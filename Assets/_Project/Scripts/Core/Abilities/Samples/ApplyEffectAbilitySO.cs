using CBuilding.StatusEffects;
using UnityEngine;

namespace CBuilding.Abilities.Samples
{
    /// <summary>
    /// Smoke-test ability: applies a GS-5 status effect to the caster on activation.
    /// Demonstrates the AbilityDataSO → AbilityRuntime pattern end-to-end and the
    /// GS-9 ↔ GS-5 integration. Use it to validate the pipeline in a test scene
    /// before real hero kits exist.
    /// </summary>
    [CreateAssetMenu(menuName = "CBuilding/Abilities/Samples/Apply Effect To Self", fileName = "Ability_ApplyEffect")]
    public class ApplyEffectAbilitySO : AbilityDataSO
    {
        [Header("Sample payload")]
        public EffectDataSO effectToApply;

        public override AbilityRuntime CreateRuntime() => new Runtime();

        private class Runtime : AbilityRuntime
        {
            public override void Execute()
            {
                var data = (ApplyEffectAbilitySO)Data;
                if (data.effectToApply == null) return;

                var status = Controller.GetComponent<StatusEffectController>();
                if (status != null)
                {
                    status.ApplyEffect(data.effectToApply, Controller.gameObject);
                    Debug.Log($"[Sample] {data.displayName} applied {data.effectToApply.displayName} to {Controller.name}");
                }
            }
        }
    }
}
