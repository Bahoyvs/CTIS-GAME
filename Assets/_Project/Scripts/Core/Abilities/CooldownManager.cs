using System;
using System.Collections.Generic;
using UnityEngine;

namespace CBuilding.Abilities
{
    /// <summary>
    /// GS-9.5 — server-side cooldown/charge bookkeeping for one AbilityController.
    /// Plain C# class, no hero-specific branching.
    ///
    /// <see cref="ReduceAllActive"/> is a SHARED public API with three known consumers:
    ///   1. Bahadır's Skill2 / Final Passive (GS-9)
    ///   2. Boss member death → group CD reduction (GS-13.1)
    ///   3. Final Phase 'Network Override' team CD reset (GS-14.4)
    /// Change its semantics only with all three in mind.
    ///
    /// <see cref="SetFrozen"/> supports GS-13.3: a stun on a boss member PAUSES its
    /// cooldown clocks (not just the current cast).
    /// </summary>
    public class CooldownManager
    {
        private class Entry
        {
            public float Remaining;   // seconds until ready (or until next charge refill)
            public float Duration;    // full cooldown length for this slot
            public int Charges;     // ChargeBased only
            public int MaxCharges;
        }

        private readonly Dictionary<AbilitySlot, Entry> _entries = new();
        private readonly Dictionary<AbilitySlot, float> _cooldownOverrides = new();
        private bool _frozen;

        /// <summary>(slot, remaining, duration) — fired on start/change so the controller can sync UI.</summary>
        public event Action<AbilitySlot, float, float> OnCooldownChanged;

        public void RegisterSlot(AbilitySlot slot, int maxCharges = 1)
        {
            _entries[slot] = new Entry
            {
                Remaining = 0f,
                Duration = 0f,
                Charges = maxCharges,
                MaxCharges = Mathf.Max(1, maxCharges),
            };
        }

        public bool IsReady(AbilitySlot slot)
        {
            if (!_entries.TryGetValue(slot, out var e)) return false;
            return e.MaxCharges > 1 ? e.Charges > 0 : e.Remaining <= 0f;
        }

        public float GetRemaining(AbilitySlot slot) =>
            _entries.TryGetValue(slot, out var e) ? Mathf.Max(0f, e.Remaining) : 0f;

        public int GetCharges(AbilitySlot slot) =>
            _entries.TryGetValue(slot, out var e) ? e.Charges : 0;

        /// <summary>
        /// SHARED API (like ReduceAllActive): while set, every Commit on <paramref name="slot"/>
        /// uses <paramref name="seconds"/> instead of the duration the caller passed. Lets
        /// timed modes rewrite a slot's cooldown without mutating the shared AbilityDataSO
        /// asset. First consumer: Gobluna's Ultimate — Skill1 CD becomes 0.4s for 18s.
        /// Callers OWN the cleanup: pair every Set with a ClearCooldownOverride.
        /// </summary>
        public void SetCooldownOverride(AbilitySlot slot, float seconds)
        {
            _cooldownOverrides[slot] = Mathf.Max(0f, seconds);
        }

        /// <summary>Removes an override set by <see cref="SetCooldownOverride"/> (no-op if none).</summary>
        public void ClearCooldownOverride(AbilitySlot slot)
        {
            _cooldownOverrides.Remove(slot);
        }

        /// <summary>Starts the cooldown (Instant/Channel/Toggle) or consumes a charge (ChargeBased).</summary>
        public void Commit(AbilitySlot slot, float cooldownDuration)
        {
            if (!_entries.TryGetValue(slot, out var e)) return;

            // A mode-driven override (Gobluna Ult) replaces the data-asset duration outright.
            if (_cooldownOverrides.TryGetValue(slot, out float overrideSeconds))
            {
                cooldownDuration = overrideSeconds;
            }

            if (e.MaxCharges > 1)
            {
                e.Charges = Mathf.Max(0, e.Charges - 1);
                if (e.Remaining <= 0f) // refill clock not already running
                {
                    e.Remaining = cooldownDuration;
                    e.Duration = cooldownDuration;
                }
            }
            else
            {
                e.Remaining = cooldownDuration;
                e.Duration = cooldownDuration;
            }

            OnCooldownChanged?.Invoke(slot, e.Remaining, e.Duration);
        }

        /// <summary>Refund part of a running cooldown (AP's early-landing mechanic).</summary>
        public void Refund(AbilitySlot slot, float seconds)
        {
            if (seconds <= 0f || !_entries.TryGetValue(slot, out var e) || e.Remaining <= 0f) return;
            e.Remaining = Mathf.Max(0f, e.Remaining - seconds);
            OnCooldownChanged?.Invoke(slot, e.Remaining, e.Duration);
        }

        /// <summary>
        /// THE shared API (see class doc). Reduces every running cooldown by
        /// <paramref name="seconds"/>. Pass float.MaxValue for a full reset (GS-14).
        /// </summary>
        public void ReduceAllActive(float seconds)
        {
            if (seconds <= 0f) return;

            foreach (var kvp in _entries)
            {
                var e = kvp.Value;
                if (e.Remaining <= 0f) continue;

                e.Remaining = Mathf.Max(0f, e.Remaining - seconds);
                if (e.Remaining <= 0f && e.MaxCharges > 1)
                {
                    RefillCharge(e);
                }
                OnCooldownChanged?.Invoke(kvp.Key, e.Remaining, e.Duration);
            }
        }

        /// <summary>GS-13.3 — while frozen (stunned boss member), cooldown clocks do not advance.</summary>
        public void SetFrozen(bool frozen) => _frozen = frozen;

        /// <summary>Call from the owning controller's server Update.</summary>
        public void Tick(float deltaTime)
        {
            if (_frozen) return;

            foreach (var kvp in _entries)
            {
                var e = kvp.Value;
                if (e.Remaining <= 0f) continue;

                e.Remaining -= deltaTime;
                if (e.Remaining <= 0f)
                {
                    e.Remaining = 0f;
                    if (e.MaxCharges > 1)
                    {
                        RefillCharge(e);
                    }
                    OnCooldownChanged?.Invoke(kvp.Key, 0f, e.Duration);
                }
            }
        }

        /// <summary>GS-2.4 run-reset hook.</summary>
        public void ResetAll()
        {
            foreach (var kvp in _entries)
            {
                var e = kvp.Value;
                e.Remaining = 0f;
                e.Charges = e.MaxCharges;
                OnCooldownChanged?.Invoke(kvp.Key, 0f, e.Duration);
            }
        }

        private void RefillCharge(Entry e)
        {
            e.Charges = Mathf.Min(e.MaxCharges, e.Charges + 1);
            if (e.Charges < e.MaxCharges)
            {
                e.Remaining = e.Duration; // keep refilling toward max
            }
        }
    }
}
