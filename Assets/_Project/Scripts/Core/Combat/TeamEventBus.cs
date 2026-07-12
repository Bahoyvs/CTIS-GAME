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

        /// <summary>
        /// (hero instigator, victim root, post-pipeline damage actually dealt). Server-only.
        /// Raised by BaseEnemy.TakeDamage for every hero-sourced hit (including DoT ticks,
        /// whose Instigator is the effect's source). First consumer: Gobluna's Siphoner
        /// passive — "whenever she deals damage, heal allies near the victim".
        /// </summary>
        public static event Action<GameObject, GameObject, float> OnAllyDealtDamage;

        public static void RaiseAllyDealtDamage(GameObject ally, GameObject victim, float amount)
        {
            if (ally == null || victim == null || amount <= 0f) return;
            OnAllyDealtDamage?.Invoke(ally, victim, amount);
        }

        /// <summary>
        /// (healer, healed hero, HP actually restored after anti-heal/overheal clamp).
        /// Server-only. Raised by HealEffectSO (composed pipeline) and by bespoke heal
        /// code (Gobluna Siphoner / AllyBounceProjectile). First consumer: Gobluna's
        /// Skill2 resource bar — "fills when Gobluna heals allies".
        /// </summary>
        public static event Action<GameObject, GameObject, float> OnAllyHealedAlly;

        public static void RaiseAllyHealedAlly(GameObject healer, GameObject target, float amount)
        {
            if (healer == null || target == null || amount <= 0f) return;
            OnAllyHealedAlly?.Invoke(healer, target, amount);
        }
    }
}
