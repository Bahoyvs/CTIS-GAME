using CBuilding.Core;
using UnityEngine;

namespace CBuilding.StatusEffects
{
    /// <summary>
    /// GS-9 (Bahadır Feature) — full immunity to melee damage specifically (not ranged/DoT/
    /// hazard), matching "100% ghost effect" without also no-selling things that logically
    /// shouldn't care whether Bahadır is walking through solid matter (a poison already in
    /// his bloodstream, standing in lava). See DamageFlags.Melee and BaseEnemy.TickAttack,
    /// currently the only melee damage source in the game.
    ///
    /// Can't use EffectDataSO.incomingDamageMultiplier for this: that field is a blanket
    /// multiplier with no flag awareness (GenericStatusEffect.Modify ignores info.Flags
    /// entirely), so this needs its own bespoke runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "CBuilding/Status Effects/Melee Invincibility", fileName = "Fx_MeleeInvincible")]
    public class MeleeInvincibilityEffectSO : EffectDataSO
    {
        public override IStatusEffect CreateRuntime() => new MeleeInvincibilityStatus(this);
    }

    /// <summary>
    /// Subclasses GenericStatusEffect purely to inherit control-flag/DoT plumbing for free
    /// (same reasoning as SpywareMarkStatus) — only the IDamageModifier half is bespoke.
    /// </summary>
    public class MeleeInvincibilityStatus : GenericStatusEffect
    {
        // Deliberately NOT reusing the base class's private _pipeline field (its OnApply
        // only registers when incomingDamageMultiplier != 1, which this effect leaves at
        // the default 1 — this effect's whole purpose IS the pipeline hook, unconditionally).
        // Managing our own reference means OnExpire can always unregister; relying on the
        // base's conditional field would leave this modifier permanently registered forever
        // after the first Stealth application — a permanent-invincibility bug.
        private DamageModifierPipeline _pipeline;

        public MeleeInvincibilityStatus(EffectDataSO data) : base(data) { }

        public override void OnApply(StatusEffectContext context)
        {
            base.OnApply(context);
            if (context.Target.TryGetComponent(out _pipeline))
                _pipeline.Register(this);
        }

        public override void OnExpire(StatusEffectContext context)
        {
            base.OnExpire(context);
            _pipeline?.Unregister(this);
            _pipeline = null;
        }

        // 200+ band ("clamps" per IDamageModifier's own priority convention) — must win
        // over multiplicative modifiers (SpywareMark etc.) that already ran on this amount.
        public override int Priority => 200;

        public override float Modify(in DamageInfo info, float currentAmount)
        {
            if (!info.IsHealing && (info.Flags & DamageFlags.Melee) != 0) return 0f;
            return base.Modify(in info, currentAmount);
        }
    }
}
