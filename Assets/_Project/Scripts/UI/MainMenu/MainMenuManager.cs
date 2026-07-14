using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;

namespace CBuilding.UI
{
    /// <summary>
    /// MainMenu scene controller: Host Game / Join Game (IP:Port over UnityTransport).
    ///
    /// FLOW
    ///   Host  : configure transport → StartHost → NGO SceneManager loads LobbyScene
    ///           (network-synced, so every later joiner is pulled into it automatically).
    ///   Client: configure transport → StartClient → NGO scene-sync moves us to LobbyScene.
    ///
    /// The player's display name is written to PlayerPrefs here and read by
    /// LobbyNetworkManager after connection (sent to the server via RPC).
    ///
    /// SETUP: lives in the MainMenu scene. Requires the persistent NetworkManager
    /// (+ UnityTransport) with "Connection Approval" ENABLED in its inspector.
    /// </summary>
    public class MainMenuManager : MonoBehaviour
    {
        public const string PlayerNamePrefKey = "cb_player_name";
        public const int MaxPlayers = 4;

        [Header("Scenes")]
        [SerializeField] private string lobbySceneName = "LobbyScene";
        [Tooltip("Joins are refused while the server sits in this scene (no mid-match late-join).")]
        [SerializeField] private string gameSceneName = "GameScene";

        [Header("UI References")]
        [SerializeField] private TMP_InputField playerNameInput;   // placeholder: "Player"
        [SerializeField] private TMP_InputField addressInput;      // placeholder: "127.0.0.1"
        [SerializeField] private TMP_InputField portInput;         // placeholder: "7777"
        [SerializeField] private Button hostButton;
        [SerializeField] private Button joinButton;
        [SerializeField] private TMP_Text statusText;

        private static string s_gameSceneName; // read by the static approval callback

        private void Start()
        {
            hostButton.onClick.AddListener(OnHostClicked);
            joinButton.onClick.AddListener(OnJoinClicked);

            if (playerNameInput != null)
                playerNameInput.text = PlayerPrefs.GetString(PlayerNamePrefKey, "");

            var nm = NetworkManager.Singleton;
            nm.OnClientDisconnectCallback += HandleLocalDisconnect;
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleLocalDisconnect;
        }

        // ---------------------------------------------------------------- Buttons

        private void OnHostClicked()
        {
            PrepareSession();
            SetInteractable(false);

            var nm = NetworkManager.Singleton;
            if (!nm.StartHost())
            {
                SetStatus("Failed to start host (port in use?).");
                SetInteractable(true);
                return;
            }

            SetStatus("Hosting — loading lobby...");
            // NGO's SceneManager, NOT UnityEngine's: replicates the load to all clients,
            // including ones that join later (scene synchronization).
            nm.SceneManager.LoadScene(lobbySceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }

        private void OnJoinClicked()
        {
            PrepareSession();
            SetInteractable(false);

            if (!NetworkManager.Singleton.StartClient())
            {
                SetStatus("Failed to start client.");
                SetInteractable(true);
                return;
            }
            SetStatus($"Connecting to {GetAddress()}:{GetPort()}...");
            // On success NGO scene-sync loads LobbyScene for us; on failure
            // OnClientDisconnectCallback fires with a DisconnectReason.
        }

        // ---------------------------------------------------------------- Session prep

        private void PrepareSession()
        {
            SavePlayerName();

            var nm = NetworkManager.Singleton;
            var transport = (UnityTransport)nm.NetworkConfig.NetworkTransport;
            transport.SetConnectionData(GetAddress(), GetPort(), listenAddress: "0.0.0.0");

            // Approval gate (max players / no mid-match joins). Static handler on purpose:
            // NetworkManager persists across scenes while THIS object dies with MainMenu —
            // an instance method here would leave a delegate pointing at a destroyed object.
            s_gameSceneName = gameSceneName;
            nm.NetworkConfig.ConnectionApproval = true;
            nm.ConnectionApprovalCallback = ApproveConnection;
        }

        private static void ApproveConnection(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            var nm = NetworkManager.Singleton;

            // The host's own local client (first request) is always approved.
            bool isHostClient = request.ClientNetworkId == NetworkManager.ServerClientId;

            if (!isHostClient && nm.ConnectedClientsList.Count >= MaxPlayers)
            {
                response.Approved = false;
                response.Reason = "Lobby is full (4/4).";
                return;
            }

            if (!isHostClient &&
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == s_gameSceneName)
            {
                response.Approved = false;
                response.Reason = "Match already in progress.";
                return;
            }

            response.Approved = true;
            response.CreatePlayerObject = false; // Lobby spawns nothing; PlayerSpawner owns GameScene spawning.
        }

        private void SavePlayerName()
        {
            string name = playerNameInput != null ? playerNameInput.text.Trim() : "";
            if (name.Length > 24) name = name[..24]; // FixedString32Bytes budget upstream.
            PlayerPrefs.SetString(PlayerNamePrefKey, name);
            PlayerPrefs.Save();
        }

        private string GetAddress()
        {
            string a = addressInput != null ? addressInput.text.Trim() : "";
            return string.IsNullOrEmpty(a) ? "127.0.0.1" : a;
        }

        private ushort GetPort()
        {
            if (portInput != null && ushort.TryParse(portInput.text.Trim(), out ushort p) && p != 0)
                return p;
            return 7777;
        }

        // ---------------------------------------------------------------- Callbacks

        private void HandleLocalDisconnect(ulong clientId)
        {
            if (clientId != NetworkManager.Singleton.LocalClientId &&
                !NetworkManager.Singleton.IsServer) return;

            string reason = NetworkManager.Singleton.DisconnectReason;
            SetStatus(string.IsNullOrEmpty(reason) ? "Disconnected." : reason);
            SetInteractable(true);
        }

        private void SetStatus(string msg)
        {
            if (statusText != null) statusText.text = msg;
        }

        private void SetInteractable(bool value)
        {
            hostButton.interactable = value;
            joinButton.interactable = value;
        }
    }
}
