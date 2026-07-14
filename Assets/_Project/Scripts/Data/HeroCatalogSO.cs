using System.Collections.Generic;
using UnityEngine;

namespace CBuilding.Data
{
    /// <summary>
    /// Single source of truth for "which heroes exist" — the lobby roster, avatar spawner
    /// and (later) PlayerSpawner all read from this ONE asset.
    ///
    /// NETWORK CONTRACT: a hero's ID is its INDEX in <see cref="Heroes"/>. That int is what
    /// travels over the wire in LobbyPlayerState, so every build must ship the same catalog
    /// in the same order. Append new heroes at the END; never reorder a shipped list.
    /// -1 always means "nothing selected".
    /// </summary>
    [CreateAssetMenu(fileName = "HeroCatalog", menuName = "C-Building/Data/Hero Catalog")]
    public class HeroCatalogSO : ScriptableObject
    {
        public const int NoSelection = -1;

        [Tooltip("Index in this list == network HeroId. Append only, never reorder.")]
        public List<HeroStatsData> Heroes = new();

        public int Count => Heroes.Count;

        public bool IsValidId(int heroId) =>
            heroId >= 0 && heroId < Heroes.Count && Heroes[heroId] != null;

        /// <summary>Null-safe lookup; returns null for NoSelection / bad ids.</summary>
        public HeroStatsData GetHero(int heroId) => IsValidId(heroId) ? Heroes[heroId] : null;

        public int GetHeroId(HeroStatsData hero) => Heroes.IndexOf(hero);

        /// <summary>Roster tab filtering. Yields (heroId, data) so UI keeps ids without lookups.</summary>
        public IEnumerable<(int heroId, HeroStatsData data)> GetHeroesByRole(HeroRole role)
        {
            for (int i = 0; i < Heroes.Count; i++)
                if (Heroes[i] != null && Heroes[i].Role == role)
                    yield return (i, Heroes[i]);
        }

        /// <summary>
        /// GDD tab labels → internal enum. Tabs read Assault/Support/Control/Defense while the
        /// stat sheets keep DPS/Support/Controller/Tank — mapped here so neither side renames.
        /// </summary>
        public static string GetRoleDisplayName(HeroRole role) => role switch
        {
            HeroRole.DPS        => "Assault",
            HeroRole.Support    => "Support",
            HeroRole.Controller => "Control",
            HeroRole.Tank       => "Defense",
            _                   => role.ToString()
        };

        /// <summary>Tab order for the lobby bottom bar (left → right).</summary>
        public static readonly HeroRole[] TabOrder =
            { HeroRole.DPS, HeroRole.Support, HeroRole.Controller, HeroRole.Tank };

#if UNITY_EDITOR
        private void OnValidate()
        {
            for (int i = 0; i < Heroes.Count; i++)
                if (Heroes[i] == null)
                    Debug.LogWarning($"[HeroCatalog] Empty slot at index {i} — ids after it are still stable, but fill it.", this);
        }
#endif
    }
}
