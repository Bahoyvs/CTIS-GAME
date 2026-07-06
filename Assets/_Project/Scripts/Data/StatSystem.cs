using System;
using UnityEngine;

namespace CBuilding.Data
{
    /// <summary>
    /// Every numeric stat in the game. Think of this as the column names of a stats table;
    /// HeroStatsData / EnemyData hold the base row values, modifiers are deltas applied on top.
    /// </summary>
    public enum StatType
    {
        MaxHealth,
        MoveSpeed,
        AttackDamage,
        AttackCooldown,
        AttackRange,
        RollSpeed,
        Armor
    }

    /// <summary>
    /// How a modifier combines with the base value.
    /// Enum values double as sort order: Flat is applied first, then PercentAdd, then PercentMult.
    /// Final = (Base + ΣFlat) * (1 + ΣPercentAdd) * Π(1 + PercentMult)
    /// </summary>
    public enum StatModType
    {
        Flat = 100,        // +25 MaxHealth
        PercentAdd = 200,  // +10% and +20% => +30% (additive stacking)
        PercentMult = 300  // +10% and +20% => *1.1 *1.2 (multiplicative stacking)
    }

    /// <summary>
    /// Author-time definition, serialized inside ItemData / SkillNodeData assets.
    /// [Serializable] makes it show up in the Inspector like an embedded document in Mongo.
    /// </summary>
    [Serializable]
    public struct StatModifierDefinition
    {
        public StatType Stat;
        public StatModType Type;
        public float Value; // For percent types use 0.1 for +10%.
    }

    /// <summary>
    /// Runtime instance of a modifier. Carries its Source (the ItemData/SkillNodeData asset
    /// that granted it) so every modifier from that source can be removed in one call.
    /// </summary>
    public class StatModifier
    {
        public readonly StatType Stat;
        public readonly StatModType Type;
        public readonly float Value;
        public readonly object Source;

        public StatModifier(in StatModifierDefinition def, object source)
        {
            Stat = def.Stat;
            Type = def.Type;
            Value = def.Value;
            Source = source;
        }
    }
}
