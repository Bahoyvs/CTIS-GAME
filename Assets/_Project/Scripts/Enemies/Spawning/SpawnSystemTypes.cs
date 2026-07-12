using System;

namespace CBuilding.Enemies.Spawning
{
    /// <summary>
    /// Physical archetype of a SpawnNode. Flags so an EncounterEntry can allow several
    /// (e.g. a Grunt may crawl out of Ground | Vent, a Ceiling Spider only Ceiling,
    /// a Desert Worm only Sand).
    /// </summary>
    [Flags]
    public enum SpawnNodeType
    {
        None    = 0,
        Ground  = 1 << 0,  // Floor cracks, doorways
        Wall    = 1 << 1,  // Wall holes
        Ceiling = 1 << 2,  // Ceiling grates — Ceiling Spider
        Vent    = 1 << 3,  // Air vents
        Sand    = 1 << 4,  // Sand mounds — Desert Worm
        Void    = 1 << 5,  // Hull breaches (Void section)
        All     = ~0,
    }

    /// <summary>
    /// Global environmental events the Director reacts to. Flags: several can be active
    /// at once (e.g. NightPhase + DebrisShower). Server sets them via
    /// <see cref="EnvironmentalEventManager"/>; SectionEncounterSO event modifiers key on them.
    /// </summary>
    [Flags]
    public enum EnvironmentalEventType
    {
        None         = 0,
        NightPhase   = 1 << 0,  // Forest: attention gain multiplied, night hunters weighted up
        Sandstorm    = 1 << 1,  // Desert: Bandit weights up
        DebrisShower = 1 << 2,  // Void: tactical enemies weighted up
        Vacuum       = 1 << 3,  // Void: no-oxygen zones active
    }
}
