using System;
using System.Collections.Generic;
using CBuilding.StatusEffects;
using UnityEngine;

namespace CBuilding.Enemies.Spawning
{
    /// <summary>
    /// One weighted entry in a section's enemy pool. Everything the Director needs to
    /// decide "can I afford this enemy, where can it come from, how likely is it".
    /// </summary>
    [Serializable]
    public class EncounterEntry
    {
        [Tooltip("BaseEnemy prefab (NetworkObject, registered in Network Prefabs).")]
        public BaseEnemy prefab;

        [Tooltip("Relative spawn probability inside its pool. 0 disables the entry.")]
        [Min(0f)] public float baseWeight = 1f;

        [Tooltip("How much Threat Capacity this enemy occupies while alive. " +
                 "Grunt ~1, Shambler ~2, Juggernaut ~8.")]
        [Min(0.1f)] public float threatCost = 1f;

        [Tooltip("Node archetypes this enemy may emerge from (Ceiling Spider -> Ceiling only, " +
                 "Desert Worm -> Sand only).")]
        public SpawnNodeType allowedNodeTypes = SpawnNodeType.Ground;

        [Tooltip("Max simultaneous instances of THIS enemy. 0 = unlimited (within capacity).")]
        [Min(0)] public int maxAlive;

        [Tooltip("Instances pre-instantiated into the network pool at section start. " +
                 "Size for the worst case (Fission chains!).")]
        [Min(0)] public int prewarmCount = 8;
    }

    /// <summary>
    /// "If event X is active, multiply the weight of these enemies by Y."
    /// Example: NightPhase -> [Stalker-Stitch, Nightstalker Predatory] x3.
    /// </summary>
    [Serializable]
    public class EventWeightModifier
    {
        public EnvironmentalEventType eventType = EnvironmentalEventType.None;

        [Tooltip("Prefabs whose weight is modified while the event is active.")]
        public List<BaseEnemy> affectedPrefabs = new();

        [Tooltip("Weight multiplier while active. 3 = three times as likely, 0 = disabled.")]
        [Min(0f)] public float weightMultiplier = 3f;
    }

    /// <summary>
    /// Data package for one Section (Forest, Desert, Void, House...). No if-else chains:
    /// the Director reads whichever asset matches SectionManager.CurrentSection and the
    /// whole encounter is authored in the Inspector.
    /// </summary>
    [CreateAssetMenu(menuName = "C-Building/Spawning/Section Encounter", fileName = "Enc_NewSection")]
    public class SectionEncounterSO : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Which SectionManager section (1-based) this asset drives.")]
        [Min(1)] public int sectionIndex = 1;

        [Header("Threat Capacity")]
        [Tooltip("Sum of threatCost of all alive director-spawned enemies may not exceed this.")]
        [Min(1f)] public float threatCapacity = 12f;

        [Tooltip("Hard cap on alive enemy COUNT, independent of threat cost.")]
        [Min(1)] public int maxAliveEnemies = 30;

        [Header("Pacing")]
        [Tooltip("Seconds between spawn attempts when under capacity (random in range).")]
        public Vector2 spawnInterval = new(2f, 5f);

        [Header("Regular Pool — spawns anywhere, weighted")]
        public List<EncounterEntry> regularPool = new();

        [Header("Special Pool — released when Attention fills")]
        [Tooltip("Section-exclusive enemies (Tribe Leader, Matriarch...). Released one at a " +
                 "time when the Attention meter reaches the threshold.")]
        public List<EncounterEntry> specialPool = new();

        [Tooltip("Attention points needed to release one special-pool enemy.")]
        [Min(1f)] public float specialAttentionThreshold = 100f;

        [Tooltip("Minimum seconds between two special releases.")]
        [Min(0f)] public float specialCooldown = 30f;

        [Tooltip("Passive attention gained per second while players are in this section.")]
        [Min(0f)] public float passiveAttentionPerSecond = 1f;

        [Tooltip("Attention multiplier while NightPhase is active (Forest design: night " +
                 "makes ability usage far more 'loud').")]
        [Min(1f)] public float nightAttentionMultiplier = 3f;

        [Header("Event Modifiers")]
        public List<EventWeightModifier> eventModifiers = new();

        [Header("Mark of Guilt (House / Family Section)")]
        [Tooltip("Status effect that marks a guilty player (looked at the paintings). " +
                 "Leave null for sections without the mechanic.")]
        public EffectDataSO guiltMarkEffect;

        [Tooltip("Enemy spawned at the node NEAREST to a guilt-marked player (Family Echo).")]
        public EncounterEntry guiltSpawnEntry;

        [Tooltip("Seconds between guilt-triggered spawns per marked player.")]
        [Min(1f)] public float guiltSpawnInterval = 12f;

        // ------------------------------------------------------------------ Queries

        /// <summary>Effective weight of an entry given the currently active events.</summary>
        public float GetEffectiveWeight(EncounterEntry entry, EnvironmentalEventType activeEvents)
        {
            float weight = entry.baseWeight;
            if (activeEvents == EnvironmentalEventType.None) return weight;

            for (int i = 0; i < eventModifiers.Count; i++)
            {
                EventWeightModifier mod = eventModifiers[i];
                if ((activeEvents & mod.eventType) == 0) continue;
                if (!mod.affectedPrefabs.Contains(entry.prefab)) continue;
                weight *= mod.weightMultiplier;
            }
            return weight;
        }

        /// <summary>All entries (regular + special + guilt) — used by the pool prewarm.</summary>
        public IEnumerable<EncounterEntry> AllEntries()
        {
            foreach (var e in regularPool) yield return e;
            foreach (var e in specialPool) yield return e;
            if (guiltSpawnEntry != null && guiltSpawnEntry.prefab != null) yield return guiltSpawnEntry;
        }
    }
}
