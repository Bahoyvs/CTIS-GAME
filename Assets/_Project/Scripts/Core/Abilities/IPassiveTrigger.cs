namespace CBuilding.Abilities
{
    /// <summary>
    /// GS-9 — contract for passives that are never player-activated (no AbilitySlot cast),
    /// so they don't fit the AbilityDataSO/AbilityRuntime request-response pipeline.
    /// Bahadır's Passive (proximity proc) and Final Passive (team-wide kill reaction) are
    /// the first implementations. Driven server-side by <see cref="PassiveController"/>.
    /// </summary>
    public interface IPassiveTrigger
    {
        /// <summary>One-time server-side setup (subscribe to events, cache components).</summary>
        void Initialize(AbilityController controller);

        /// <summary>Server: called every PassiveController tick while owner is alive.</summary>
        void ServerTick(float deltaTime);

        /// <summary>Server: unsubscribe / cleanup when the owner despawns.</summary>
        void Shutdown();
    }
}
