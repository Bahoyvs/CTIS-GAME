using UnityEngine;

namespace CBuilding.Data
{
    /// <summary>
    /// GDD class roles — drives HUD class color coding (GS-16):
    /// Tank = Ironworks/Ug, DPS = Kerem/AP, Controller = Bahadır/Ok, Support = TL/Gobluna.
    /// </summary>
    public enum HeroRole : byte
    {
        Tank = 0,
        DPS = 1,
        Controller = 2,
        Support = 3
    }

    /// <summary>
    /// Base stat sheet for a hero. One asset per hero (Kerem.asset, etc.) — treat it like a
    /// read-only DB schema row. Runtime state (current HP, applied modifiers) never lives here;
    /// ScriptableObject assets persist edits made in Play Mode in the Editor, so mutating them
    /// at runtime is a classic Unity footgun.
    /// </summary>
    [CreateAssetMenu(fileName = "NewHeroStats", menuName = "C-Building/Data/Hero Stats")]
    public class HeroStatsData : ScriptableObject
    {
        [Header("Identity")]
        public string HeroName = "Unnamed Hero";
        [TextArea] public string Description;
        [Tooltip("GDD class — drives HUD class color coding (GS-16).")]
        public HeroRole Role = HeroRole.DPS;

        [Header("Presentation (Lobby & UI)")]
        [Tooltip("Portrait used in the lobby roster grid and top-bar slots.")]
        public Sprite Icon;
        [Tooltip("Visual-only prefab shown at the lobby desks: SpriteRenderer/Animator only, " +
                 "NO NetworkObject, NO controllers. Falls back to a bare Icon sprite if empty.")]
        public GameObject LobbyAvatarPrefab;
        [Tooltip("The real networked hero prefab spawned in GameScene (must be a registered " +
                 "Network Prefab). Read by PlayerSpawner via LobbyNetworkManager.HeroSelections.")]
        public GameObject GameplayPrefab;

        [Header("Vitals")]
        [Min(1f)] public float MaxHealth = 100f;
        [Min(0f)] public float Armor = 0f;

        [Header("Movement")]
        [Min(0f)] public float MoveSpeed = 6f;
        [Min(0f)] public float RollSpeed = 14f;

        [Header("Basic Attack")]
        [Min(0f)] public float AttackDamage = 10f;
        [Tooltip("Seconds between basic attacks.")]
        [Min(0.05f)] public float AttackCooldown = 0.5f;
        [Tooltip("Reach of the melee hitbox, in world units.")]
        [Min(0.1f)] public float AttackRange = 1.5f;
        [Min(0f)] public float KnockbackForce = 6f;

        /// <summary>Maps the enum to the authored field. Single source of truth for base values.</summary>
        public float GetBaseValue(StatType stat)
        {
            switch (stat)
            {
                case StatType.MaxHealth:      return MaxHealth;
                case StatType.MoveSpeed:      return MoveSpeed;
                case StatType.AttackDamage:   return AttackDamage;
                case StatType.AttackCooldown: return AttackCooldown;
                case StatType.AttackRange:    return AttackRange;
                case StatType.RollSpeed:      return RollSpeed;
                case StatType.Armor:          return Armor;
                default:
                    Debug.LogWarning($"[HeroStatsData] No base value defined for {stat} on {name}.");
                    return 0f;
            }
        }
    }
}
