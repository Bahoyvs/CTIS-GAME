namespace CBuilding.Abilities
{
    /// <summary>
    /// GS-9.2 — the standard slot contract shared by all 8 heroes.
    /// (Roll stays on BaseHero/HeroController — it is not a cooldown-gated
    /// ability in the GDD sense.)
    ///
    /// GS-17: BasicAttack IS a slot now, so CooldownManager owns its clock like
    /// every other ability. Its duration is re-read from
    /// Stats.GetStat(StatType.AttackCooldown) on every trigger — never baked.
    /// </summary>
    public enum AbilitySlot : byte
    {
        Feature,      // right-click
        Passive,
        FinalPassive,
        Skill1,
        Skill2,
        Ultimate,
        BasicAttack   // GS-17 — cooldown re-read from Stats every trigger
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
