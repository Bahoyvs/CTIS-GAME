using UnityEngine;

namespace CBuilding.Audio
{
    /// <summary>
    /// One biome's complete horizontal-sequencing music set (GS-xx / MusicDirector).
    /// All 4 clips MUST be authored at the same BPM / time signature and trimmed to
    /// whole bars (plus an optional lead-in, see <see cref="exploreLoopLeadIn"/> etc.).
    ///
    /// Create one asset per biome (Forest, Void, Frozen, Desert, Family) via
    /// Create → CBuilding → Audio → Music Clip Set.
    /// </summary>
    [CreateAssetMenu(menuName = "CBuilding/Audio/Music Clip Set", fileName = "MCS_Biome")]
    public class MusicClipSetSO : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Purely informational — shown in MusicDirector's debug overlay.")]
        public string biomeName = "Unnamed Biome";

        [Header("Clips (all at the same BPM, trimmed to whole bars)")]
        [Tooltip("Calm loop. Plays indefinitely while threat is low.")]
        public AudioClip exploreLoop;

        [Tooltip("Build-up stinger. Plays exactly ONCE bridging Explore → Combat.")]
        public AudioClip combatTransition;

        [Tooltip("High-energy loop. Plays indefinitely while threat is high.")]
        public AudioClip combatLoop;

        [Tooltip("Cooldown stinger. Plays exactly ONCE bridging Combat → Explore.")]
        public AudioClip combatEnd;

        [Header("Timing")]
        [Tooltip("Tempo shared by all 4 clips. MUST match the DAW project's BPM exactly.")]
        [Min(1f)] public float bpm = 120f;

        [Tooltip("Beats per bar (time signature numerator). 4 for 4/4, 3 for 3/4, etc.")]
        [Min(1)] public int beatsPerBar = 4;

        [Header("Per-clip lead-in compensation (seconds)")]
        [Tooltip("Silence / transient bleed at the START of the clip file, before the " +
                 "musical downbeat. The scheduler skips this so the downbeat lands " +
                 "exactly on the scheduled dspTime. 0 for perfectly trimmed exports.")]
        [Min(0f)] public float exploreLoopLeadIn;
        [Min(0f)] public float combatTransitionLeadIn;
        [Min(0f)] public float combatLoopLeadIn;
        [Min(0f)] public float combatEndLeadIn;

        [Header("Duration policy")]
        [Tooltip("ON (recommended): each clip's musical duration is snapped to the nearest " +
                 "whole bar count, so tiny export-length inaccuracies (a few ms of padding) " +
                 "never accumulate into drift. OFF: raw (clip.length - leadIn) is used.")]
        public bool quantizeDurationToWholeBars = true;

        // ------------------------------------------------------------------ Derived

        /// <summary>60 / BPM.</summary>
        public double SecondsPerBeat => 60.0 / bpm;

        /// <summary>SecondsPerBeat * BeatsPerBar.</summary>
        public double SecondsPerBar => SecondsPerBeat * beatsPerBar;

        /// <summary>All 4 clips assigned and timing values sane.</summary>
        public bool IsValid =>
            exploreLoop != null && combatTransition != null &&
            combatLoop != null && combatEnd != null &&
            bpm > 0f && beatsPerBar > 0;

        public AudioClip GetClip(MusicRole role) => role switch
        {
            MusicRole.ExploreLoop      => exploreLoop,
            MusicRole.CombatTransition => combatTransition,
            MusicRole.CombatLoop       => combatLoop,
            _                          => combatEnd,
        };

        public float GetLeadIn(MusicRole role) => role switch
        {
            MusicRole.ExploreLoop      => exploreLoopLeadIn,
            MusicRole.CombatTransition => combatTransitionLeadIn,
            MusicRole.CombatLoop       => combatLoopLeadIn,
            _                          => combatEndLeadIn,
        };

        /// <summary>
        /// Musical duration of a clip in seconds: (file length - lead-in), optionally
        /// snapped to the nearest whole bar count (never less than one bar).
        /// This is the duration the scheduler advances by — NOT the raw file length —
        /// so back-to-back segments stay bar-aligned over long sessions.
        /// </summary>
        public double GetMusicalDuration(MusicRole role)
        {
            AudioClip clip = GetClip(role);
            if (clip == null) return 0.0;

            double raw = (double)clip.samples / clip.frequency - GetLeadIn(role);
            if (!quantizeDurationToWholeBars) return raw;

            double bar = SecondsPerBar;
            long bars = (long)System.Math.Round(raw / bar);
            if (bars < 1) bars = 1;
            return bars * bar;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Warn when a clip's trimmed length is noticeably off a whole bar count —
            // that usually means either the BPM field is wrong or the export has
            // untrimmed padding that needs a LeadIn value.
            const double toleranceSeconds = 0.02; // ~1 frame of audio sloppiness
            WarnIfOffGrid(MusicRole.ExploreLoop,      toleranceSeconds);
            WarnIfOffGrid(MusicRole.CombatTransition, toleranceSeconds);
            WarnIfOffGrid(MusicRole.CombatLoop,       toleranceSeconds);
            WarnIfOffGrid(MusicRole.CombatEnd,        toleranceSeconds);
        }

        private void WarnIfOffGrid(MusicRole role, double tolerance)
        {
            AudioClip clip = GetClip(role);
            if (clip == null || bpm <= 0f || beatsPerBar <= 0) return;

            double raw = (double)clip.samples / clip.frequency - GetLeadIn(role);
            double bar = SecondsPerBar;
            double offGrid = System.Math.Abs(raw - System.Math.Round(raw / bar) * bar);
            if (offGrid > tolerance)
            {
                Debug.LogWarning(
                    $"[MusicClipSetSO:{name}] '{clip.name}' ({role}) is {offGrid * 1000.0:F1} ms " +
                    $"off a whole-bar length at {bpm} BPM {beatsPerBar}/4. Check BPM, or set " +
                    "the clip's LeadInOffsetSeconds to compensate export padding.", this);
            }
        }
#endif
    }

    /// <summary>Which of the 4 structural clips a scheduled segment plays.</summary>
    public enum MusicRole { ExploreLoop, CombatTransition, CombatLoop, CombatEnd }
}
