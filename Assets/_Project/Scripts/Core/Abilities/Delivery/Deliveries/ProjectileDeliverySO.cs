using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Abilities.Delivery
{
    /// <summary>
    /// Spawns 1..N networked projectiles toward the aim point.
    ///   Kerem S1  = count 1 + explosionRadius (exploding energy ball)
    ///   Gobluna S1= count 3, small spreadAngle, pierceCount 99 (piercing arrows,
    ///               effects: Damage→Enemies + Heal→Allies)
    ///   TL S2     = count 8, radialSpread (vines in all directions)
    ///   AP S1     = count 1 (marking bullet → mark via ApplyStatusEffectSO)
    /// </summary>
    [CreateAssetMenu(menuName = "CBuilding/Abilities/Deliveries/Projectile", fileName = "Del_Projectile")]
    public class ProjectileDeliverySO : AbilityDeliverySO
    {
        [Header("Prefab (AbilityProjectile + NetworkObject + NetworkTransform, in Network Prefabs list)")]
        public NetworkObject projectilePrefab;

        [Header("Volley")]
        [Min(1)] public int count = 1;
        [Tooltip("Total fan angle for count > 1. Ignored when radialSpread is on.")]
        [Range(0f, 180f)] public float spreadAngle = 20f;
        [Tooltip("Distribute projectiles over a full 360° circle (TL's 8-way vines).")]
        public bool radialSpread = false;

        [Header("Flight")]
        [Min(0.1f)] public float speed = 14f;
        [Min(0.5f)] public float maxRange = 12f;
        [Tooltip("How many targets one projectile can hit before despawning. 1 = stops on first hit; 99 ≈ full pierce.")]
        [Min(1)] public int pierceCount = 1;
        [Tooltip("If > 0: on each hit (and at max range), also detonate an AoE of this radius.")]
        [Min(0f)] public float explosionRadius = 0f;
        public LayerMask hitLayers = ~0;

        public override void Execute(in AbilityCastContext ctx)
        {
            if (projectilePrefab == null)
            {
                Debug.LogWarning($"[{name}] No projectilePrefab assigned.");
                return;
            }

            Vector3 baseDir = ctx.AimPoint - ctx.Origin;
            baseDir.y = 0f;
            baseDir = baseDir.sqrMagnitude > 0.01f ? baseDir.normalized : ctx.Caster.transform.forward;

            for (int i = 0; i < count; i++)
            {
                float angle;
                if (radialSpread)
                {
                    angle = (360f / count) * i;
                }
                else
                {
                    // Fan centered on the aim direction: -spread/2 .. +spread/2
                    angle = count > 1 ? Mathf.Lerp(-spreadAngle * 0.5f, spreadAngle * 0.5f, i / (count - 1f)) : 0f;
                }

                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * baseDir;
                Vector3 spawnPos = ctx.Origin + dir * 0.6f + Vector3.up * 0.5f;

                NetworkObject instance = Object.Instantiate(
                    projectilePrefab, spawnPos, Quaternion.LookRotation(dir));

                if (instance.TryGetComponent<AbilityProjectile>(out var projectile))
                {
                    projectile.ServerConfigure(ctx.Ability, this, ctx.Caster, dir); // BEFORE Spawn
                }
                instance.Spawn(true);
            }
        }
    }
}
