namespace CBuilding.Abilities
{
    /// <summary>
    /// GS-9.2 — the standard slot contract shared by all 8 heroes.
    /// (BasicAttack and Roll stay on BaseHero/HeroController — they are not
    /// cooldown-gated abilities in the GDD sense.)
    /// </summary>
    public enum AbilitySlot : byte
    {
        Feature,      // right-click
        Passive,
        FinalPassive,
        Skill1,
        Skill2,
        Ultimate
    }

    /// <summary>
    /// GS-9.4 — activation mode variety lives HERE, in data, not as hero-specific
    /// branches in AbilityController/CooldownManager.
    /// Instant: fire &amp; start cooldown.
    /// Channel: Execute → ChannelTick for channelDuration → ChannelEnd → cooldown.
    ///          (AP's 'early landing refunds CD' = runtime calls EndChannelEarly with refund.)
    /// Toggle: Execute on, ToggleEnd on second press → cooldown starts on toggle-off (Ug's wall).
    /// ChargeBased: N charges, each activation consumes one; charges refill on cooldown
    ///              (Kerem's 10-stack passive models stacks as charges).
    /// </summary>
    public enum AbilityMode : byte
    {
        Instant,
        Channel,
        Toggle,
        ChargeBased
    }
}
