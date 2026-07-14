using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using CBuilding.Data;

namespace CBuilding.Lobby
{
    /// <summary>
    /// One cell in the bottom roster grid. Instantiated once per catalog hero by
    /// LobbyUIManager; tab switching toggles visibility, never rebuilds.
    /// </summary>
    public class HeroRosterButtonUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Button button;
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private GameObject selectedFrame; // highlight: local player's pick
        [SerializeField] private GameObject lockedOverlay; // dimmer/padlock: taken by someone else

        public int HeroId { get; private set; }
        public HeroRole Role { get; private set; }

        public void Init(int heroId, HeroStatsData hero, Action<int> onClicked)
        {
            HeroId = heroId;
            Role = hero.Role;

            nameText.text = hero.HeroName;
            if (hero.Icon != null) iconImage.sprite = hero.Icon;

            button.onClick.AddListener(() => onClicked?.Invoke(HeroId));
            SetState(takenByOther: false, selectedByLocal: false, interactable: true);
        }

        public void SetState(bool takenByOther, bool selectedByLocal, bool interactable)
        {
            selectedFrame.SetActive(selectedByLocal);
            lockedOverlay.SetActive(takenByOther);
            button.interactable = interactable && !takenByOther;
        }
    }
}
