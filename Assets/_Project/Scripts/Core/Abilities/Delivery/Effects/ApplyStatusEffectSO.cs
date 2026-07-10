using System;
using CBuilding.StatusEffects;
using UnityEngine;

namespace CBuilding.Abilities.Delivery
{
    /// <summary>
    /// Applies a GS-5 status effect: your poison DoT, Bahadır's Spyware mark & stuns/slows,
    /// Ok's blind, buffs like move-speed (make a non-debuff EffectDataSO with
    /// moveSpeedMultiplier &gt; 1 and appliesTo = AlliesAndSelf).
    /// </summary>
    [CreateAssetMenu(menuName = "CBuilding/Abilities/Effects/Apply Status", fileName = "Fx_Status")]
    public class ApplyStatusEffectSO : AbilityEffectSO
    {
        public EffectDataSO statusEffect;

        /// <summary>
        /// Fired every time ANY ApplyStatusEffectSO successfully lands a status through the
        /// composed pipeline (caster, target, effect data). Generic cross-system hook — e.g.
        /// Bahadır's Skill2 listens for "a Bahadır-sourced Stun landed" without the Stun
        /// asset itself needing to know Bahadır exists.
        /// </summary>
        public static event Action<EffectDataSO, GameObject, GameObject> OnAnyStatusApplied;

        protected override void OnApply(in EffectContext ctx)
        {
            if (statusEffect == null) return;

            if (ctx.Target.TryGetComponent<StatusEffectController>(out var controller))
            {
                controller.ApplyEffect(statusEffect, ctx.Caster);
                OnAnyStatusApplied?.Invoke(statusEffect, ctx.Caster, ctx.Target);
            }
        }
    }
}
