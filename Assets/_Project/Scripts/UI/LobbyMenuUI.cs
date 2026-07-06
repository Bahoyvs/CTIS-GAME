using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace CBuilding.UI
{
    /// <summary>
    /// Minimal main-menu controller: Create Game (Host) / Join Game (Client).
    /// Pure view layer — all session logic lives in NetworkSessionManager.
    ///
    /// SETUP (Canvas):
    ///   Panel "LobbyPanel"
    ///     ├─ Button "CreateGameButton"
    ///     ├─ Button "JoinGameButton"
    ///     ├─ TMP_InputField "AddressInput"  (placeholder: 127.0.0.1)
    ///     └─ TMP_Text "StatusText"
    /// </summary>
    public class LobbyMenuUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject lobbyPanel;
        [SerializeField] private Button createGameButton;
        [SerializeField] private Button joinGameButton;
        [SerializeField] private TMP_InputField addressInput;
        [SerializeField] private TMP_Text statusText;

        private void Start()
        {
            createGameButton.onClick.AddListener(OnCreateGameClicked);
            joinGameButton.onClick.AddListener(OnJoinGameClicked);

            if (CBuilding.Network.NetworkSessionManager.Instance != null)
                CBuilding.Network.NetworkSessionManager.Instance.OnSessionEvent += SetStatus;

            // Hide the lobby the moment OUR local client is in — host or joiner alike.
            NetworkManager.Singleton.OnClientConnectedCallback += HandleLocalConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleLocalDisconnected;
        }

        private void OnDestroy()
        {
            if (CBuilding.Network.NetworkSessionManager.Instance != null)
                CBuilding.Network.NetworkSessionManager.Instance.OnSessionEvent -= SetStatus;

            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= HandleLocalConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleLocalDisconnected;
            }
        }

        // ---------------------------------------------------------------- Button handlers

        private void OnCreateGameClicked()
        {
            SetInteractable(false);
            if (!CBuilding.Network.NetworkSessionManager.Instance.StartHost())
                SetInteractable(true);
        }

        private void OnJoinGameClicked()
        {
            SetInteractable(false);
            string address = addressInput != null ? addressInput.text : "127.0.0.1";
            if (!CBuilding.Network.NetworkSessionManager.Instance.StartClient(address))
                SetInteractable(true);
        }

        // ---------------------------------------------------------------- Session callbacks

        private void HandleLocalConnected(ulong clientId)
        {
            // Callback fires for every client on the server; only react to OURSELVES.
            if (clientId != NetworkManager.Singleton.LocalClientId) return;
            lobbyPanel.SetActive(false); // Straight into gameplay — PlayerSpawner has spawned us.
        }

        private void HandleLocalDisconnected(ulong clientId)
        {
            if (clientId != NetworkManager.Singleton.LocalClientId) return;
            lobbyPanel.SetActive(true); // Back to menu on disconnect/failed join.
            SetInteractable(true);
        }

        private void SetStatus(string message)
        {
            if (statusText != null) statusText.text = message;
        }

        private void SetInteractable(bool value)
        {
            createGameButton.interactable = value;
            joinGameButton.interactable = value;
        }
    }
}
