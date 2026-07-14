using UnityEngine;
using CBuilding.Data;

namespace CBuilding.Lobby
{
    /// <summary>
    /// The "physical lobby room": renders each player's selected hero as a 2D figure at
    /// their desk. PURELY LOCAL VISUALS — nothing here is a NetworkObject and nothing is
    /// network-spawned. Every client runs this independently off the same replicated
    /// NetworkList, so everyone sees identical desks without paying replication cost for
    /// what is effectively a fancy portrait.
    ///
    /// Desk index == lobby slot index == join order (matches the top-bar slots, so the
    /// portrait above and the figure below always agree).
    ///
    /// SETUP: plain scene object in LobbyScene, 4 desk Transforms assigned, catalog assigned.
    /// </summary>
    public class LobbyAvatarSpawner : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private HeroCatalogSO heroCatalog;

        [Header("Scene — 4 desk anchor points, left to right")]
        [SerializeField] private Transform[] deskPoints = new Transform[4];

        [Header("Fallback (used when a hero has no LobbyAvatarPrefab)")]
        [Tooltip("Sorting layer for fallback SpriteRenderers.")]
        [SerializeField] private string fallbackSortingLayer = "Default";

        private LobbyNetworkManager _lobby;

        // Cache per desk so we only destroy/instantiate when the hero actually changed.
        private readonly int[] _spawnedHeroIds = { -2, -2, -2, -2 }; // -2 = "nothing spawned yet"
        private readonly GameObject[] _spawnedAvatars = new GameObject[4];

        private void Start()
        {
            _lobby = LobbyNetworkManager.Instance;
            if (_lobby == null)
            {
                Debug.LogError("[LobbyAvatarSpawner] No LobbyNetworkManager in scene.", this);
                return;
            }

            _lobby.OnLobbyChanged += RefreshDesks;
            RefreshDesks();
        }

        private void OnDestroy()
        {
            if (_lobby != null) _lobby.OnLobbyChanged -= RefreshDesks;
        }

        // ---------------------------------------------------------------- Core

        private void RefreshDesks()
        {
            for (int desk = 0; desk < deskPoints.Length; desk++)
            {
                // Empty desk when no player occupies the slot OR they haven't picked yet.
                int desiredHeroId = HeroCatalogSO.NoSelection;
                if (_lobby.TryGetStateAtSlot(desk, out LobbyPlayerState state))
                    desiredHeroId = state.SelectedHeroId;

                if (desiredHeroId == _spawnedHeroIds[desk]) continue; // no change → no churn

                ClearDesk(desk);

                if (desiredHeroId != HeroCatalogSO.NoSelection)
                    SpawnAvatar(desk, desiredHeroId);

                _spawnedHeroIds[desk] = desiredHeroId;
            }
        }

        private void SpawnAvatar(int desk, int heroId)
        {
            HeroStatsData hero = heroCatalog.GetHero(heroId);
            if (hero == null) return;

            Transform anchor = deskPoints[desk];
            GameObject avatar;

            if (hero.LobbyAvatarPrefab != null)
            {
                // Plain Instantiate ON PURPOSE — a NetworkObject.Spawn here would replicate
                // a spawn we're already deriving locally on every client from the list.
                avatar = Instantiate(hero.LobbyAvatarPrefab, anchor.position, anchor.rotation, anchor);
            }
            else
            {
                // Art not made yet? Stand the roster portrait at the desk so flow is testable.
                avatar = new GameObject($"Avatar_{hero.HeroName}");
                avatar.transform.SetParent(anchor, false);
                var sr = avatar.AddComponent<SpriteRenderer>();
                sr.sprite = hero.Icon;
                sr.sortingLayerName = fallbackSortingLayer;
            }

            _spawnedAvatars[desk] = avatar;
        }

        private void ClearDesk(int desk)
        {
            if (_spawnedAvatars[desk] != null)
            {
                Destroy(_spawnedAvatars[desk]);
                _spawnedAvatars[desk] = null;
            }
        }
    }
}
