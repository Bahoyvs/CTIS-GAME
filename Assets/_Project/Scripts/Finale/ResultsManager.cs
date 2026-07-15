using CBuilding.Network;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Finale
{
    /// <summary>
    /// Win/Lose ekranı + rematch/lobby dönüşü. FinaleManager.Resolved fazını dinler,
    /// Victory bayrağına göre paneli açar. Networked state taşımaz — saf sunum;
    /// otorite FinaleManager/NetworkGameManager'dadır.
    ///
    /// SETUP: Finale sahnesindeki UI canvas'ına ekle; panelleri ve buton onClick'lerini
    /// (HostRematch / LeaveToMainMenu) Inspector'dan bağla.
    /// </summary>
    public class ResultsManager : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject winPanel;
        [SerializeField] private GameObject losePanel;

        [Header("Host-only UI (Rematch butonu vb.)")]
        [SerializeField] private GameObject hostControls;

        [Header("Scenes")]
        [Tooltip("Rematch: host'un network-load edeceği lobby sahnesi.")]
        [SerializeField] private string lobbySceneName = "LobbyScene";

        [Tooltip("Leave: lokal dönüş sahnesi (LobbyNetworkManager ile aynı isim olmalı).")]
        [SerializeField] private string mainMenuSceneName = "MainMenu";

        // Start (OnEnable değil): tüm Awake'ler bitmiş, FinaleManager.Instance garanti dolu.
        private void Start()
        {
            if (FinaleManager.Instance != null)
                FinaleManager.Instance.OnPhaseChanged += HandlePhaseChanged;
            SetPanels(false, false);
        }

        private void OnDestroy()
        {
            if (FinaleManager.Instance != null)
                FinaleManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
        }

        private void HandlePhaseChanged(FinalePhase phase)
        {
            if (phase != FinalePhase.Resolved) { SetPanels(false, false); return; }

            bool victory = FinaleManager.Instance.Victory;
            SetPanels(victory, !victory);

            if (hostControls != null)
                hostControls.SetActive(NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer);
        }

        private void SetPanels(bool win, bool lose)
        {
            if (winPanel != null) winPanel.SetActive(win);
            if (losePanel != null) losePanel.SetActive(lose);
        }

        // ---------------------------------------------------------------- Buttons

        /// <summary>HOST ONLY: session'ı Lobby state'ine çevirip lobby sahnesini network-load eder.</summary>
        public void HostRematch()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;

            NetworkGameManager.Instance?.ResetToLobby();
            nm.SceneManager.LoadScene(lobbySceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }

        /// <summary>Herkes için: session'dan çık, lokal ana menüye dön (LobbyNetworkManager.LeaveLobby paterni).</summary>
        public void LeaveToMainMenu()
        {
            NetworkManager.Singleton?.Shutdown();
            UnityEngine.SceneManagement.SceneManager.LoadScene(mainMenuSceneName);
        }
    }
}
