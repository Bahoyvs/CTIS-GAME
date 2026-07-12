using System;
using System.Collections.Generic;
using UnityEngine;

namespace CBuilding.Abilities
{
    /// <summary>
    /// GS-17 §7.3 — shared "every Nth basic attack" component, so Cleave Sweep,
    /// Recycler Claws and Rapid Needle don't reinvent three ad hoc counters.
    /// SERVER-only state: call the Register* methods from server-side attack code
    /// (behaviour Fire(), projectile hit callbacks).
    ///
    /// Rec #11 — reset rules split by archetype KIND, not one universal rule:
    ///   - Cast-streak (Cleave Sweep / Recycler Claws): pure modulo counting, NO decay
    ///     by default (decayWindow = 0). "Every 3rd hit" as a readable rhythm cue
    ///     matches Trinity Brawler's existing cyclical design.
    ///   - Per-target (Rapid Needle): decays ~4.5s without a hit ON THAT TARGET, so it
    ///     can't leave invisible sleeper marks on enemies who've left combat.
    /// </summary>
    public class BasicAttackComboTracker : MonoBehaviour
    {
        [Header("Cast streak (Cleave Sweep S2, Recycler Claws S2)")]
        [Tooltip("The N in 'every Nth attack'.")]
        [Min(2)] [SerializeField] private int streakN = 3;
        [Tooltip("Rec #11: 0 = never decays (pure modulo, recommended for cast-streak archetypes). > 0 = streak resets after this many seconds without a cast.")]
        [Min(0f)] [SerializeField] private float streakDecayWindow = 0f;

        [Header("Per-target hits (Rapid Needle S2)")]
        [Tooltip("The N in 'Nth consecutive hit on the same target'.")]
        [Min(2)] [SerializeField] private int perTargetN = 5;
        [Tooltip("Rec #11: per-target counters reset after this many seconds without hitting that specific target.")]
        [Min(0.5f)] [SerializeField] private float perTargetDecaySeconds = 4.5f;

        /// <summary>Fired on the Nth cast (server-only). The archetype's Section-tier companion subscribes to apply the bonus (360° arc, Double Hit...).</summary>
        public event Action OnStreakTriggered;

        /// <summary>Fired on the Nth consecutive hit on one target (server-only). Payload: that target.</summary>
        public event Action<GameObject> OnPerTargetTriggered;

        private int _streak;
        private float _lastCastTime = float.NegativeInfinity;

        private class TargetEntry { public int Hits; public float LastHitTime; }
        private readonly Dictionary<GameObject, TargetEntry> _perTarget = new();
        private readonly List<GameObject> _expiredScratch = new();

        /// <summary>Current position in the 1..N cycle (for HUD pips / anim selection). 1-based.</summary>
        public int StreakPosition => (_streak % streakN) + 1;

        // ---- Cast streak ----

        /// <summary>
        /// SERVER: call once per basic-attack CAST (not per target hit). Returns true
        /// when this cast is the Nth — callers may branch directly instead of using the
        /// event (ComposedBasicAttackBehaviour companions prefer the event).
        /// </summary>
        public bool RegisterCast()
        {
            if (streakDecayWindow > 0f && Time.time - _lastCastTime > streakDecayWindow)
                _streak = 0;

            _lastCastTime = Time.time;
            _streak++;

            if (_streak % streakN != 0) return false;
            OnStreakTriggered?.Invoke();
            return true;
        }

        /// <summary>External break rule hook (if Kerem later rules that stuns/target swaps break streaks).</summary>
        public void ResetStreak() => _streak = 0;

        // ---- Per-target (Rapid Needle variant — its counter is per-ENEMY, not per-cast) ----

        /// <summary>
        /// SERVER: call once per basic-attack HIT on a specific target. Returns true on
        /// the Nth consecutive hit; the counter for that target then restarts.
        /// Hitting a DIFFERENT enemy neither pauses nor resets this one — only the
        /// per-target decay clears it (rec #11).
        /// </summary>
        public bool RegisterHit(GameObject target)
        {
            if (target == null) return false;
            PruneExpired();

            if (!_perTarget.TryGetValue(target, out var entry))
            {
                entry = new TargetEntry();
                _perTarget[target] = entry;
            }

            entry.Hits++;
            entry.LastHitTime = Time.time;

            if (entry.Hits < perTargetN) return false;

            entry.Hits = 0;
            OnPerTargetTriggered?.Invoke(target);
            return true;
        }

        private void PruneExpired()
        {
            _expiredScratch.Clear();
            foreach (var kvp in _perTarget)
            {
                // Destroyed targets (died, despawned) or decayed counters — no sleeper marks.
                if (kvp.Key == null || Time.time - kvp.Value.LastHitTime > perTargetDecaySeconds)
                    _expiredScratch.Add(kvp.Key);
            }
            for (int i = 0; i < _expiredScratch.Count; i++)
                _perTarget.Remove(_expiredScratch[i]);
        }

        /// <summary>GS-2.4 run-reset hook.</summary>
        public void ResetAll()
        {
            _streak = 0;
            _perTarget.Clear();
        }
    }
}
