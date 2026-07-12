using UnityEngine;
using CBuilding.Core;
using CBuilding.Heroes;
using CBuilding.StatusEffects;

namespace CBuilding.Enemies
{
    /// <summary>
    /// Projectile volley attack. One component covers every roster ranged profile:
    ///   Tri-Archer      — count 3, spread 25° (center/left/right cone)
    ///   Rail-Spitter    — count 1, piercing (slug through everyone in the line)
    ///   Poison Weaver   — count 1, onHitEffect = stacking poison
    ///   Curse-Binder    — count 1, onHitEffect = anti-heal, slow cadence via EnemyData
    ///   Phoenix-Ghoul   — count 3, narrow spread (fireball volley)
    ///   Heavy Gunner    — count 1, tiny damage, cadence 0.3s via EnemyData (suppression)
    ///   Spit Bile       — count 1, impactPuddle = slowing micro-puddle
    /// Cadence comes from EnemyData.AttackCooldown (via RosterEnemy); this fires ONE volley.
    /// </summary>
    public class EnemyRangedAttack : EnemyAttackBehaviour
    {
        [Header("Projectile")]
        [Tooltip("EnemyProjectile prefab (NetworkObject, in Network Prefabs).")]
        [SerializeField] private EnemyProjectile projectilePrefab;

        [Min(1)] [SerializeField] private int projectileCount = 1;

        [Tooltip("Total volley cone in degrees; projectiles are spaced evenly across it.")]
        [Range(0f, 180f)] [SerializeField] private float spreadAngle = 0f;

        [Min(0f)] [SerializeField] private float damage = 5f;
        [Min(0.5f)] [SerializeField] private float projectileSpeed = 10f;

        [Tooltip("Rail-Spitter: fly through every hero (and geometry), hitting each once.")]
        [SerializeField] private bool piercing;

        [Header("On Hit")]
        [Tooltip("Status effect applied to each hero hit (poison, anti-heal...).")]
        [SerializeField] private EffectDataSO onHitEffect;

        [Tooltip("Hazard puddle dropped where a non-piercing shot lands (Spit Bile).")]
        [SerializeField] private EnemyHazardZone impactPuddlePrefab;

        [Header("Launch")]
        [Min(0f)] [SerializeField] private float muzzleHeight = 0.6f;
        [Min(1f)] [SerializeField] private float maxRange = 14f;

        public override void ExecuteAttack(RosterEnemy owner, BaseHero target)
        {
            if (projectilePrefab == null || target == null) return;

            Vector3 baseDir = target.transform.position - owner.transform.position;
            baseDir.y = 0f;
            if (baseDir.sqrMagnitude < 0.001f) return;
            baseDir.Normalize();

            Vector3 origin = owner.transform.position + Vector3.up * muzzleHeight;

            for (int i = 0; i < projectileCount; i++)
            {
                float yaw = projectileCount > 1
                    ? Mathf.Lerp(-spreadAngle * 0.5f, spreadAngle * 0.5f, i / (float)(projectileCount - 1))
                    : 0f;
                Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * baseDir;

                EnemyProjectile projectile = Instantiate(
                    projectilePrefab, origin, Quaternion.LookRotation(dir));
                projectile.ServerInit(dir, projectileSpeed, damage, piercing, onHitEffect,
                    impactPuddlePrefab, owner.gameObject, maxRange, owner.transform.position.y);
                projectile.NetworkObject.Spawn(true);
            }

            CombatLogManager.LogAction(owner.DisplayName, "used",
                $"Ranged_Attack on {target.DisplayName}", owner.transform.position);
        }
    }
}
