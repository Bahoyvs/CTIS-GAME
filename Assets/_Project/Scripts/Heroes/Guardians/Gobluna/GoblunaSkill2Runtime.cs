using CBuilding.Abilities;
using UnityEngine;

namespace CBuilding.Heroes.Gobluna
{
    /// <summary>
    /// Logic half of <see cref="GoblunaSkill2DataSO"/> — a deliberately thin bridge.
    /// All real state (lock, resource bar, burning set) lives in the sibling
    /// GoblunaSkill2Controller NetworkBehaviour, because AbilityRuntime instances are
    /// plain C# objects and cannot own NetworkVariables. The runtime only translates
    /// the GS-9 slot hooks into calls on that component.
    /// </summary>
    public class GoblunaSkill2Runtime : AbilityRuntime
    {
        private GoblunaSkill2Controller _skill2;

        protected override void OnInitialize()
        {
            _skill2 = Controller.GetComponent<GoblunaSkill2Controller>();
            if (_skill2 == null)
            {
                Debug.LogError(
                    "[GoblunaSkill2Runtime] Gobluna's prefab needs a GoblunaSkill2Controller " +
                    "next to AbilityController — Skill2 will refuse to cast without it.",
                    Controller);
            }
        }

        /// <summary>The lock/resource gate. Cooldown is 0, so THIS is the only gate.</summary>
        public override bool CanActivate() => _skill2 != null && _skill2.CanCast;

        public override void Execute()
        {
            var data = (GoblunaSkill2DataSO)Data;
            _skill2.ServerCast(data.coneAbility, Controller, Controller.CurrentAimPoint);
        }
    }
}
