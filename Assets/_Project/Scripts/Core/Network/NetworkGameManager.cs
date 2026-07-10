using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Network
{
    /// <summary>
    /// GS-1 — Networking &amp; Session Foundation.
    /// Server-authoritative session STATE + 4-player tracking singleton.
    ///
    /// Division of labor with NetworkSessionManager (same GameObject family):
    ///   NetworkSessionManager = transport facade (StartHost/StartClient/Shutdown, UI events).
    ///   NetworkGameManager    = replicated session state (Lobby/InRun/RunComplete),
    ///                           player registry, connection approval (4-player cap).
    ///
    /// Every other global system reads session state from here; only the server writes.
    /// </summary>
    public class NetworkGameManager : NetworkBehaviour
    {
        public const int MaxPlayers = 4;

        public static NetworkGameManager Instance { get; private set; }

        /// <summary>
        /// Coarse session state. The full run FSM (Section1 → Boss → ...) belongs to
        /// SectionManager (GS-2); this only gates "is a run active at all".
        /// </summary>
        public enum SessionState : byte
        {
            Lobby,
            InRun,
            RunComplete
        }

        /// <summary>GS-1.3 canonical pattern: NetworkVariable, server-write / everyone-read.</summary>
        private readonly NetworkVariable<SessionState> _sessionState = new(
            SessionState.Lobby,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public SessionState State => _sessionState.Value;

        /// <summary>Raised on server AND clients whenever session state changes.</summary>
        public event Action<SessionState, SessionState> OnSessionStateChanged;

        /// <summary>Server-only: raised when a player connects / disconnects.</summary>
        public event Action<ulong> OnPlayerJoined;
        public event Action<ulong> OnPlayerLeft;

        /// <summary>Server-only registry: clientId → that client's hero root object.</summary>
        private readonly Dictionary<ulong, NetworkObject> _playerObjects = new();

        public IReadOnlyDictionary<ulong, NetworkObject> PlayerObjects => _playerObjects;
        public int PlayerCount => _playerObjects.Count;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            _sessionState.OnValueChanged += HandleStateChanged;

            if (IsServer)
            {
                NetworkManager.OnConnectionEvent += HandleConnectionEvent;

                // Register anyone already connected (host joins before spawn).
                foreach (var clientId in NetworkManager.ConnectedClientsIds)
                {
                    RegisterPlayer(clientId);
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            _sessionState.OnValueChanged -= HandleStateChanged;

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnConnectionEvent -= HandleConnectionEvent;
            }
        }

        private void HandleStateChanged(SessionState previous, SessionState current)
        {
            OnSessionStateChanged?.Invoke(previous, current);
        }

        private void HandleConnectionEvent(NetworkManager nm, ConnectionEventData data)
        {
            switch (data.EventType)
            {
                case ConnectionEvent.ClientConnected:
                    RegisterPlayer(data.ClientId);
                    break;
                case ConnectionEvent.ClientDisconnected:
                    UnregisterPlayer(data.ClientId);
                    break;
            }
        }

        private void RegisterPlayer(ulong clientId)
        {
            if (_playerObjects.ContainsKey(clientId)) return;

            NetworkObject playerObject = null;
            if (NetworkManager.ConnectedClients.TryGetValue(clientId, out var client))
            {
                playerObject = client.PlayerObject; // may be null if spawned later
            }

            _playerObjects[clientId] = playerObject;
            OnPlayerJoined?.Invoke(clientId);
        }

        private void UnregisterPlayer(ulong clientId)
        {
            if (_playerObjects.Remove(clientId))
            {
                OnPlayerLeft?.Invoke(clientId);
            }
        }

        /// <summary>
        /// Server-only: late-bind a hero object to a client (PlayerSpawner calls this
        /// after spawning; hero-select flows can rebind later).
        /// </summary>
        public void SetPlayerObject(ulong clientId, NetworkObject hero)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[NetworkGameManager] SetPlayerObject is server-only.");
                return;
            }
            _playerObjects[clientId] = hero;
        }

        /// <summary>
        /// Hook this into NetworkManager's ConnectionApproval to enforce the 4-player cap.
        /// </summary>
        public void HandleConnectionApproval(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            bool hasRoom = NetworkManager.ConnectedClientsIds.Count < MaxPlayers;
            bool inLobby = _sessionState.Value == SessionState.Lobby;

            response.Approved = hasRoom && inLobby;
            response.CreatePlayerObject = false; // PlayerSpawner owns spawning
            if (!response.Approved)
            {
                response.Reason = hasRoom ? "Run already in progress." : "Session is full.";
            }
        }

        /// <summary>Server-only session transitions.</summary>
        public void StartRun()
        {
            if (!IsServer) return;
            _sessionState.Value = SessionState.InRun;
        }

        public void CompleteRun()
        {
            if (!IsServer) return;
            _sessionState.Value = SessionState.RunComplete;
        }

        /// <summary>
        /// GS-2.4 run-reset hook: SectionManager (and the XP/skill-tree system) subscribe to
        /// OnSessionStateChanged and reset their NetworkVariables when this fires.
        /// </summary>
        public void ResetToLobby()
        {
            if (!IsServer) return;
            _sessionState.Value = SessionState.Lobby;
        }
    }
}
