using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using CBuilding.Data;

namespace CBuilding.Lobby
{
    /// <summary>
    /// One row of lobby state per connected player. Lives in a NetworkList, so it must be
    /// an unmanaged struct: FixedString32Bytes instead of string, int hero id instead of
    /// an asset reference. IEquatable is required by NetworkList's change detection.
    /// </summary>
    public struct LobbyPlayerState : INetworkSerializable, IEquatable<LobbyPlayerState>
    {
        public ulong ClientId;
        public FixedString32Bytes PlayerName;
        public int SelectedHeroId;   // index into HeroCatalogSO.Heroes; -1 = none
        public bool IsReady;

        public LobbyPlayerState(ulong clientId, FixedString32Bytes playerName, int selectedHeroId, bool isReady)
        {
            ClientId = clientId;
            PlayerName = playerName;
            SelectedHeroId = selectedHeroId;
            IsReady = isReady;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref PlayerName);
            serializer.SerializeValue(ref SelectedHeroId);
            serializer.SerializeValue(ref IsReady);
        }

        public bool Equals(LobbyPlayerState other) =>
            ClientId == other.ClientId &&
            PlayerName.Equals(other.PlayerName) &&
            SelectedHeroId == other.SelectedHeroId &&
            IsReady == other.IsReady;

        public override bool Equals(object obj) => obj is LobbyPlayerState o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(ClientId, PlayerName, SelectedHeroId, IsReady);
    }

    /// <summary>
    /// Authoritative lobby state. SERVER mutates the NetworkList (join/leave/select/ready);
    /// CLIENTS request changes via RPCs and render whatever the replicated list says.
    /// UI and the desk-avatar spawner both subscribe to <see cref="OnLobbyChanged"/> and do
    /// a full refresh — with ≤4 rows, diffing is complexity with no payoff.
    ///
    /// SETUP: scene object in LobbyScene with a NetworkObject component. In-scene-placed
    /// NetworkObjects are owned by the server, hence every client→server RPC here.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class LobbyNetworkManager : NetworkBehaviour
    {
        public static LobbyNetworkManager Instance { get; private set; }

        [Header("Data")]
        [SerializeField] private HeroCatalogSO heroCatalog;

        [Header("Scenes")]
        [SerializeField] private string gameSceneName = "GameScene";
        [SerializeField] private string mainMenuSceneName = "MainMenu";

        /// <summary>Fired on every replicated change (join, leave, select, ready). UI refresh hook.</summary>
        public event Action OnLobbyChanged;

        /// <summary>
        /// SERVER-ONLY handoff to GameScene: clientId → heroId, snapshotted the moment the host
        /// presses Start. PlayerSpawner reads this (via heroCatalog.GetHero(id).GameplayPrefab)
        /// instead of a hardcoded prefab. Static so it survives the scene switch without DDOL.
        /// </summary>
        public static readonly Dictionary<ulong, int> HeroSelections = new();

        // NetworkList MUST be constructed before spawn (field initializer / Awake), never lazily.
        private readonly NetworkList<LobbyPlayerState> _players = new();

        public HeroCatalogSO Catalog => heroCatalog;
        public int PlayerCount => _players.Count;

        // ---------------------------------------------------------------- Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            _players.OnListChanged += HandleListChanged;

            if (IsServer)
            {
                NetworkManager.OnClientConnectedCallback += HandleClientConnected;
                NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;

                HeroSelections.Clear(); // stale data from a previous match

                // Host loaded LobbyScene after clients connected? Backfill everyone present.
                foreach (var client in NetworkManager.ConnectedClientsList)
                    AddPlayer(client.ClientId);
            }

            if (IsClient)
            {
                // Push our display name (saved by MainMenuManager) up to the server.
                string name = PlayerPrefs.GetString("cb_player_name", "");
                if (string.IsNullOrWhiteSpace(name)) name = $"Player {NetworkManager.LocalClientId}";

                // CopyFromTruncated, not the implicit cast: the cast THROWS if the UTF-8 bytes
                // exceed 29 (easy to hit with non-ASCII names, e.g. Turkish characters).
                FixedString32Bytes fixedName = default;
                fixedName.CopyFromTruncated(name);
                SetPlayerNameServerRpc(fixedName);

                // Getting kicked / host quitting should drop us back to the main menu.
                NetworkManager.OnClientDisconnectCallback += HandleLocalDisconnected;
            }

            OnLobbyChanged?.Invoke(); // initial paint (late joiners receive the full list on spawn)
        }

        public override void OnNetworkDespawn()
        {
            _players.OnListChanged -= HandleListChanged;
            if (NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
                NetworkManager.OnClientDisconnectCallback -= HandleLocalDisconnected;
            }
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy(); // NetworkBehaviour.OnDestroy disposes NetworkLists — always call it.
        }

        // ---------------------------------------------------------------- Server bookkeeping

        private void HandleClientConnected(ulong clientId) => AddPlayer(clientId);

        private void AddPlayer(ulong clientId)
        {
            if (TryGetIndex(clientId, out _)) return; // idempotent: backfill loop vs callback race
            _players.Add(new LobbyPlayerState(clientId, $"Player {clientId}", HeroCatalogSO.NoSelection, false));
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (TryGetIndex(clientId, out int i))
                _players.RemoveAt(i); // frees their hero lock implicitly — it's just list state
        }

        private void HandleLocalDisconnected(ulong clientId)
        {
            if (IsServer || clientId != NetworkManager.LocalClientId) return;
            // Local (not networked) scene load: the session is already gone.
            UnityEngine.SceneManagement.SceneManager.LoadScene(mainMenuSceneName);
        }

        private void HandleListChanged(NetworkListEvent<LobbyPlayerState> _) => OnLobbyChanged?.Invoke();

        // ---------------------------------------------------------------- Client → Server RPCs

        [Rpc(SendTo.Server)]
        private void SetPlayerNameServerRpc(FixedString32Bytes playerName, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!TryGetIndex(sender, out int i)) return;

            var state = _players[i];
            state.PlayerName = playerName.IsEmpty ? new FixedString32Bytes($"Player {sender}") : playerName;
            _players[i] = state; // structs: mutate a copy, write back to trigger replication
        }

        [Rpc(SendTo.Server)]
        private void SelectHeroServerRpc(int heroId, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!TryGetIndex(sender, out int i)) return;
            if (!heroCatalog.IsValidId(heroId)) return;            // never trust client ints
            if (IsHeroTakenByOther(heroId, sender)) return;        // UNIQUE-SELECTION LOCK
            if (_players[i].IsReady) return;                       // no swapping after ready

            var state = _players[i];
            state.SelectedHeroId = heroId;
            _players[i] = state;
        }

        [Rpc(SendTo.Server)]
        private void SetReadyServerRpc(bool ready, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!TryGetIndex(sender, out int i)) return;

            var state = _players[i];
            if (ready && state.SelectedHeroId == HeroCatalogSO.NoSelection) return; // must pick first
            state.IsReady = ready;
            _players[i] = state;
        }

        // ---------------------------------------------------------------- Public API (UI calls these)

        public void SelectHeroLocal(int heroId) => SelectHeroServerRpc(heroId);

        public void ToggleReadyLocal()
        {
            if (TryGetState(NetworkManager.LocalClientId, out var me))
                SetReadyServerRpc(!me.IsReady);
        }

        /// <summary>HOST ONLY. Snapshots selections for PlayerSpawner, then network-loads GameScene.</summary>
        public void StartGame()
        {
            if (!IsServer) return;
            if (!AreAllPlayersReady()) return; // UI should prevent this; server re-checks anyway

            HeroSelections.Clear();
            foreach (var p in _players)
                HeroSelections[p.ClientId] = p.SelectedHeroId;

            NetworkManager.SceneManager.LoadScene(gameSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }

        /// <summary>Leave button: works for host (ends session for all) and client alike.</summary>
        public void LeaveLobby()
        {
            NetworkManager.Shutdown();
            UnityEngine.SceneManagement.SceneManager.LoadScene(mainMenuSceneName);
        }

        // ---------------------------------------------------------------- Queries (UI reads these)

        /// <summary>Player at lobby slot [0..3]; slot order == join order. False = empty slot.</summary>
        public bool TryGetStateAtSlot(int slot, out LobbyPlayerState state)
        {
            if (slot >= 0 && slot < _players.Count) { state = _players[slot]; return true; }
            state = default;
            return false;
        }

        public bool TryGetState(ulong clientId, out LobbyPlayerState state)
        {
            if (TryGetIndex(clientId, out int i)) { state = _players[i]; return true; }
            state = default;
            return false;
        }

        public bool IsHeroTakenByOther(int heroId, ulong askingClientId)
        {
            if (heroId == HeroCatalogSO.NoSelection) return false;
            foreach (var p in _players)
                if (p.SelectedHeroId == heroId && p.ClientId != askingClientId)
                    return true;
            return false;
        }

        public bool AreAllPlayersReady()
        {
            if (_players.Count == 0) return false;
            foreach (var p in _players)
                if (!p.IsReady) return false;
            return true;
        }

        private bool TryGetIndex(ulong clientId, out int index)
        {
            for (int i = 0; i < _players.Count; i++)
                if (_players[i].ClientId == clientId) { index = i; return true; }
            index = -1;
            return false;
        }
    }
}
