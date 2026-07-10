using System;
using UnityEngine;
using CBuilding.Enemies;

namespace CBuilding.Core
{
    /// <summary>
    /// Static, server-only event bus for team-wide reactions that don't belong to any single
    /// entity. First consumer: Bahadır's Final Passive (GS-9) — "whenever ANY ally lands the
    /// killing blow on an enemy, grant the whole roster a CD-reduction + speed pulse."
    /// BaseEnemy.Die() raises this using the last recorded damage instigator.
    /// </summary>
    public static class TeamEventBus
    {
        /// <summary>(ally who landed the kill, the enemy that died). Server-only.</summary>
        public static event Action<GameObject, BaseEnemy> OnAllyKilledEnemy;

        public static void RaiseAllyKilledEnemy(GameObject ally, BaseEnemy enemy)
        {
            if (ally == null || enemy == null) return;
            OnAllyKilledEnemy?.Invoke(ally, enemy);
        }
    }
}
