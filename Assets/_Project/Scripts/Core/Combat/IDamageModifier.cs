namespace CBuilding.Core
{
    /// <summary>
    /// GS-5.4 — one link in the damage/heal modifier chain on a target entity.
    /// SpywareMark, Mark of Guilt, Sunburn's increased-damage, Troll's anti-heal:
    /// all are implementations of this interface registered on the target's
    /// <see cref="DamageModifierPipeline"/>. No special cases in damage functions.
    /// </summary>
    public interface IDamageModifier
    {
        /// <summary>Lower runs first. Suggested bands: 0-99 flat, 100-199 multiplicative, 200+ clamps.</summary>
        int Priority { get; }

        /// <summary>Return the adjusted amount. <paramref name="currentAmount"/> is the running total.</summary>
        float Modify(in DamageInfo info, float currentAmount);
    }
}
