using System;
using System.Collections.Generic;
using UnityEngine;
using CBuilding.Data;

namespace CBuilding.Heroes
{
    /// <summary>
    /// Runtime stat sheet for a hero. Reads immutable base values from HeroStatsData and
    /// layers StatModifiers (from items / skill nodes) on top — comparable to a base config
    /// object merged with runtime overrides.
    ///
    /// Values are cached and only recomputed when a modifier is added/removed (dirty flag),
    /// so GetStat() is safe to call every frame from movement/combat code.
    /// </summary>
    public class HeroStatController : MonoBehaviour
    {
        [SerializeField] private HeroStatsData baseStats;

        /// <summary>Fired whenever a stat's final value changes. BaseHero listens for MaxHealth.</summary>
        public event Action<StatType> OnStatChanged;

        public HeroStatsData BaseStats => baseStats;

        private readonly Dictionary<StatType, List<StatModifier>> _modifiers = new();
        private readonly Dictionary<StatType, float> _cache = new();
        private readonly HashSet<StatType> _dirty = new();
        private readonly HashSet<SkillNodeData> _unlockedNodes = new();

        private void Awake()
        {
            if (baseStats == null)
                Debug.LogError($"[HeroStatController] No HeroStatsData assigned on {name}.", this);
        }

        // ---------------------------------------------------------------- Queries

        public float GetStat(StatType stat)
        {
            if (_dirty.Contains(stat) || !_cache.TryGetValue(stat, out float value))
            {
                value = Calculate(stat);
                _cache[stat] = value;
                _dirty.Remove(stat);
            }
            return value;
        }

        public bool HasUnlocked(SkillNodeData node) => _unlockedNodes.Contains(node);

        public bool CanUnlock(SkillNodeData node)
        {
            if (node == null || _unlockedNodes.Contains(node)) return false;
            foreach (SkillNodeData prereq in node.Prerequisites)
                if (!_unlockedNodes.Contains(prereq)) return false;
            return true;
        }

        // ---------------------------------------------------------------- Mutations

        /// <summary>Apply an item's modifiers. Uses the asset itself as the removal key.</summary>
        public void ApplyItem(ItemData item)
        {
            if (item == null) return;
            ApplyModifiers(item.Modifiers, item);
        }

        /// <summary>Remove everything a non-consumable item granted (unequip).</summary>
        public void RemoveItem(ItemData item) => RemoveModifiersFromSource(item);

        /// <summary>Unlock a skill node if prerequisites are met. Returns false otherwise.</summary>
        public bool ApplySkillNode(SkillNodeData node)
        {
            if (!CanUnlock(node)) return false;
            _unlockedNodes.Add(node);
            ApplyModifiers(node.Modifiers, node);
            return true;
        }

        public void ApplyModifiers(IReadOnlyList<StatModifierDefinition> defs, object source)
        {
            for (int i = 0; i < defs.Count; i++)
            {
                var mod = new StatModifier(defs[i], source);
                if (!_modifiers.TryGetValue(mod.Stat, out List<StatModifier> list))
                {
                    list = new List<StatModifier>();
                    _modifiers[mod.Stat] = list;
                }
                list.Add(mod);
                MarkDirty(mod.Stat);
            }
        }

        public void RemoveModifiersFromSource(object source)
        {
            if (source == null) return;
            foreach (var kvp in _modifiers)
            {
                int removed = kvp.Value.RemoveAll(m => ReferenceEquals(m.Source, source));
                if (removed > 0) MarkDirty(kvp.Key);
            }
        }

        // ---------------------------------------------------------------- Internals

        private void MarkDirty(StatType stat)
        {
            _dirty.Add(stat);
            OnStatChanged?.Invoke(stat);
        }

        /// <summary>Final = (Base + ΣFlat) * (1 + ΣPercentAdd) * Π(1 + PercentMult)</summary>
        private float Calculate(StatType stat)
        {
            float value = baseStats != null ? baseStats.GetBaseValue(stat) : 0f;

            if (!_modifiers.TryGetValue(stat, out List<StatModifier> list) || list.Count == 0)
                return value;

            float flat = 0f, percentAdd = 0f, percentMult = 1f;
            for (int i = 0; i < list.Count; i++)
            {
                switch (list[i].Type)
                {
                    case StatModType.Flat:        flat += list[i].Value; break;
                    case StatModType.PercentAdd:  percentAdd += list[i].Value; break;
                    case StatModType.PercentMult: percentMult *= 1f + list[i].Value; break;
                }
            }

            value = (value + flat) * (1f + percentAdd) * percentMult;
            return Mathf.Max(0f, value);
        }
    }
}
