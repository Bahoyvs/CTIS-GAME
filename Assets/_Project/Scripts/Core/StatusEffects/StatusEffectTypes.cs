using System;

namespace CBuilding.StatusEffects
{
    /// <summary>
    /// GS-5.2 — per-effect stacking policy.
    /// StackDuration: re-apply extends remaining time (Scarab poison).
    /// StackIntensity: re-apply adds a stack, magnitude scales (Troll anti-heal).
    /// Refresh: re-apply resets duration, single instance.
    /// Ignore: re-apply while active does nothing.
    /// </summary>
    public enum StackingPolicy : byte
    {
        Refresh,
        StackDuration,
        StackIntensity,
        Ignore
    }

    /// <summary>
    /// Hard control states granted by effects. Aggregated by StatusEffectController;
    /// movement/ability/input systems query the aggregate, never individual effects.
    /// </summary>
    [Flags]
    public enum ControlFlags
    {
        None = 0,
        Stun = 1 << 0,      // no move, no abilities, no attacks
        Root = 1 << 1,      // no move (Vine Webs)
        Silence = 1 << 2,   // no abilities (Father)
        Blind = 1 << 3,     // vision obscured (Spore Pockets)
        Freeze = 1 << 4,    // Ice Shell — stun + visual shell
        Isolate = 1 << 5,   // Mother — all sensory input cut
        Stealth = 1 << 6,   // Bahadır Feature — hidden from enemy targeting while untouched by threat
    }
}
