using UnityEngine;

namespace CBuilding.Data
{
    /// <summary>
    /// Base stat sheet for an enemy archetype. One asset per enemy type
    /// (saved under Assets/_Project/Data/Enemies/).
    /// </summary>
    [CreateAssetMenu(fileName = "NewEnemyData", menuName = "C-Building/Data/Enemy Data")]
    public class EnemyData : ScriptableObject
    {
        [Header("Identity")]
        public string EnemyName = "Unnamed Enemy";

        [Header("Vitals")]
        [Min(1f)] public float MaxHealth = 30f;

        [Header("Movement / AI")]
        [Min(0f)] public float MoveSpeed = 3.5f;
        [Tooltip("Player distance at which the enemy switches Idle -> Chase.")]
        [Min(0f)] public float AggroRange = 8f;
        [Tooltip("Distance at which Chase -> Attack.")]
        [Min(0f)] public float AttackRange = 1.6f;

        [Header("Attack")]
        [Min(0f)] public float AttackDamage = 5f;
        [Min(0.1f)] public float AttackCooldown = 1.2f;

        [Header("Hit Reaction")]
        [Tooltip("Seconds the NavMeshAgent is paused when this enemy is hit.")]
        [Min(0f)] public float HitStunDuration = 0.25f;
        [Tooltip("Multiplier on incoming knockback force. 0 = immune (heavies/bosses).")]
        [Min(0f)] public float KnockbackResistanceMultiplier = 1f;
    }
}
