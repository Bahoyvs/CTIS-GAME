using TMPro;
using UnityEngine;
using UnityEngine.UI;
using CBuilding.Data;

namespace CBuilding.Lobby
{
    /// <summary>
    /// One of the 4 top-bar slots: name, connection status, ready state, hero portrait.
    /// Dumb view — LobbyUIManager pushes state in, nothing here talks to the network.
    /// </summary>
    public class LobbyPlayerSlotUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text statusText;      // "Connected" / "Empty"
        [SerializeField] private Image heroIcon;
        [SerializeField] private GameObject readyIndicator; // checkmark / "READY" badge
        [SerializeField] private CanvasGroup canvasGroup;   // dim empty slots

        public void SetEmpty()
        {
            nameText.text = "—";
            statusText.text = "Empty";
            readyIndicator.SetActive(false);
            heroIcon.enabled = false;
            if (canvasGroup != null) canvasGroup.alpha = 0.35f;
        }

        public void Bind(in LobbyPlayerState state, HeroStatsData hero, bool isLocalPlayer)
        {
            nameText.text = isLocalPlayer ? $"{state.PlayerName} (You)" : state.PlayerName.ToString();
            statusText.text = "Connected";
            readyIndicator.SetActive(state.IsReady);

            if (hero != null && hero.Icon != null)
            {
                heroIcon.sprite = hero.Icon;
                heroIcon.enabled = true;
            }
            else
            {
                heroIcon.enabled = false; // no pick yet
            }

            if (canvasGroup != null) canvasGroup.alpha = 1f;
        }
    }
}
