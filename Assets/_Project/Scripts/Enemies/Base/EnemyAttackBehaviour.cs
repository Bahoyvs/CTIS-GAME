using UnityEngine;
using CBuilding.Heroes;

namespace CBuilding.Enemies
{
    /// <summary>
    /// Pluggable attack executed by RosterEnemy.TickAttack when the enemy is in Attack
    /// state and off cooldown. One per prefab (first found wins). SERVER-ONLY call site —
    /// implementations may mutate hero state directly through server mutators.
    /// Cadence stays owned by RosterEnemy (data.AttackCooldown / attack-speed mods);
    /// implementations decide only WHAT a single attack is.
    /// </summary>
    public abstract class EnemyAttackBehaviour : MonoBehaviour
    {
        public abstract void ExecuteAttack(RosterEnemy owner, BaseHero target);
    }
}
