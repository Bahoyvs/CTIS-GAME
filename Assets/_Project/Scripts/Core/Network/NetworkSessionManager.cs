using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CBuilding.Network
{
    /// <summary>
    /// Thin facade over NetworkManager for session lifecycle: start Host, join as Client,
    /// optional networked scene transition. UI talks to THIS, never to NetworkManager
    /// directly — same idea as putting a service layer in front of a driver.
    ///
    /// SETUP: place on the same GameObject as NetworkManager (+ UnityTransport).
    /// Leave NetworkManager's "Default Player Prefab" EMPTY — PlayerSpawner owns spawning.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class NetworkSessionManager : MonoBehaviour
    {
        public static NetworkSessionManager Instance { get; private set; }

        [Header("Connection")]
        [SerializeField] private ushort port = 7777;

        [Header("Scene Flow")]
        [Tooltip("If set, the server loads this scene (network-synced) right after hosting. " +
                 "Leave empty for single-scene MVP where menu and gameplay coexist.")]
        [SerializeField] private string gameplaySceneName = "";

        /// <summary>Local session state changed (started/stopped/failed). UI subscribes.</summary>
        public event Action<string> OnSessionEvent;

        private NetworkManager _netManager;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _netManager = GetComponent<NetworkManager>();
        }

        private void Start()
        {
            // Server-side connect/disconnect bookkeeping. These fire on the server for every
            // client, and on each client for itself.
            _netManager.OnClientConnectedCallback += HandleClientConnected;
            _netManager.OnClientDisconnectCallback += HandleClientDisconnected;
            _netManager.OnTransportFailure += HandleTransportFailure;
        }

        private void OnDestroy()
        {
            if (_netManager == null) return;
            _netManager.OnClientConnectedCallback -= HandleClientConnected;
            _netManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            _netManager.OnTransportFailure -= HandleTransportFailure;
        }

        // ---------------------------------------------------------------- Public API

        /// <summary>Start as Host = Server + local Client in one process (listen server).</summary>
        public bool StartHost()
        {
            ConfigureTransport("0.0.0.0"); // Listen on all interfaces.
            bool ok = _netManager.StartHost();
            if (ok)
            {
                OnSessionEvent?.Invoke("Hosting game...");
                LoadGameplaySceneIfConfigured();
            }
            else
            {
                OnSessionEvent?.Invoke("Failed to start host.");
            }
            return ok;
        }

        /// <summary>Join a host at the given address. Scene sync then happens automatically.</summary>
        public bool StartClient(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) address = "127.0.0.1";
            ConfigureTransport(address.Trim());

            bool ok = _netManager.StartClient();
            OnSessionEvent?.Invoke(ok ? $"Connecting to {address}..." : "Failed to start client.");
            return ok;
        }

        public void Shutdown()
        {
            _netManager.Shutdown();
            OnSessionEvent?.Invoke("Session ended.");
        }

        // ---------------------------------------------------------------- Internals

        private void ConfigureTransport(string address)
        {
            // UnityTransport is NGO's default UDP transport; connection data must be set
            // BEFORE StartHost/StartClient.
            var transport = (UnityTransport)_netManager.NetworkConfig.NetworkTransport;
            transport.SetConnectionData(address, port, listenAddress: "0.0.0.0");
        }

        private void LoadGameplaySceneIfConfigured()
        {
            if (string.IsNullOrEmpty(gameplaySceneName)) return;
            if (SceneManager.GetActiveScene().name == gameplaySceneName) return;

            // CRITICAL: use NGO's SceneManager, NOT UnityEngine.SceneManager — this replicates
            // the load to all connected/late-joining clients and re-syncs NetworkObjects.
            _netManager.SceneManager.LoadScene(gameplaySceneName, LoadSceneMode.Single);
        }

        private void HandleClientConnected(ulong clientId)
        {
            OnSessionEvent?.Invoke(_netManager.IsServer
                ? $"Client {clientId} connected ({_netManager.ConnectedClientsList.Count}/4)."
                : "Connected to host.");
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            // On a pure client this also fires for ITSELF on connection failure/kick.
            string reason = string.IsNullOrEmpty(_netManager.DisconnectReason)
                ? "Disconnected." : _netManager.DisconnectReason;
            OnSessionEvent?.Invoke(_netManager.IsServer ? $"Client {clientId} left." : reason);
        }

        private void HandleTransportFailure() => OnSessionEvent?.Invoke("Transport failure — session closed.");
    }
}
