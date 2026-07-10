using System.Collections.Generic;
using UnityEngine;
using CBuilding.StatusEffects;

namespace CBuilding.Enemies
{
    /// <summary>
    /// Server-only lookup: "which enemies currently carry status runtime T", used by kits
    /// that need to act on an already-applied mark without re-targeting (Bahadır Skill1's
    /// chain-stab bonus, Skill2's chain-mark). Scans the scene rather than requiring every
    /// BaseEnemy to self-register — simplest correct option while enemy counts are MVP-small;
    /// revisit with an explicit register/unregister list if enemy counts grow large.
    /// </summary>
    public static class EnemyRegistry
    {
        /// <summary>All alive enemies whose StatusEffectController has an active runtime of type T.</summary>
        public static List<BaseEnemy> GetAllWithEffect<T>() where T : class, IStatusEffect
        {
            var result = new List<BaseEnemy>();

            foreach (BaseEnemy enemy in Object.FindObjectsByType<BaseEnemy>(FindObjectsSortMode.None))
            {
                if (enemy == null || !enemy.IsAlive) continue;
                if (!enemy.TryGetComponent<StatusEffectController>(out var status)) continue;
                if (status.GetActiveEffectOfType<T>() != null) result.Add(enemy);
            }
            return result;
        }
    }
}
