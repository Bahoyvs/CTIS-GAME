using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using CBuilding.Data;

namespace CBuilding.Lobby
{
    /// <summary>
    /// LobbyScene view layer. Owns the canvas: 4 top-bar player slots, role-tab roster grid,
    /// Ready / Start Game button. All state comes from LobbyNetworkManager's replicated list;
    /// this class never mutates anything itself, it only sends requests down and repaints on
    /// OnLobbyChanged. Full repaint every change — 4 players, not worth diffing.
    /// </summary>
    public class LobbyUIManager : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private HeroCatalogSO heroCatalog;

        [Header("Top Bar — exactly 4, slot order == join order")]
        [SerializeField] private LobbyPlayerSlotUI[] playerSlots = new LobbyPlayerSlotUI[4];

        [Header("Roster (Bottom Bar)")]
        [SerializeField] private HeroRosterButtonUI heroButtonPrefab;
        [SerializeField] private Transform rosterGridParent;      // has GridLayoutGroup
        [Tooltip("Order must match HeroCatalogSO.TabOrder: Assault, Support, Control, Defense.")]
        [SerializeField] private Button[] roleTabButtons = new Button[4];
        [SerializeField] private Color tabActiveColor = Color.white;
        [SerializeField] private Color tabInactiveColor = new(1f, 1f, 1f, 0.45f);

        [Header("Actions")]
        [SerializeField] private Button readyStartButton;
        [SerializeField] private TMP_Text readyStartLabel;
        [SerializeField] private Button leaveButton;
        [SerializeField] private TMP_Text hintText; // "Pick a hero", "Waiting for players..." etc.

        private readonly List<HeroRosterButtonUI> _heroButtons = new();
        private LobbyNetworkManager _lobby;

        // ---------------------------------------------------------------- Lifecycle

        private void Start()
        {
            _lobby = LobbyNetworkManager.Instance;
            if (_lobby == null)
            {
                Debug.LogError("[LobbyUI] No LobbyNetworkManager in scene.", this);
                return;
            }

            BuildRoster();
            HookButtons();

            _lobby.OnLobbyChanged += Repaint;
            SetActiveTab(HeroRole.DPS);
            Repaint();
        }

        private void OnDestroy()
        {
            if (_lobby != null) _lobby.OnLobbyChanged -= Repaint;
        }

        // ---------------------------------------------------------------- Build (once)

        private void BuildRoster()
        {
            for (int id = 0; id < heroCatalog.Count; id++)
            {
                HeroStatsData hero = heroCatalog.GetHero(id);
                if (hero == null) continue;

                HeroRosterButtonUI btn = Instantiate(heroButtonPrefab, rosterGridParent);
                btn.Init(id, hero, OnHeroClicked);
                _heroButtons.Add(btn);
            }
        }

        private void HookButtons()
        {
            for (int i = 0; i < roleTabButtons.Length && i < HeroCatalogSO.TabOrder.Length; i++)
            {
                HeroRole role = HeroCatalogSO.TabOrder[i];

                // Tab labels come from the mapping, so the buttons can stay generic in the prefab.
                var label = roleTabButtons[i].GetComponentInChildren<TMP_Text>();
                if (label != null) label.text = HeroCatalogSO.GetRoleDisplayName(role);

                roleTabButtons[i].onClick.AddListener(() => SetActiveTab(role));
            }

            readyStartButton.onClick.AddListener(OnReadyStartClicked);
            if (leaveButton != null) leaveButton.onClick.AddListener(() => _lobby.LeaveLobby());
        }

        // ---------------------------------------------------------------- Input handlers

        private void OnHeroClicked(int heroId) => _lobby.SelectHeroLocal(heroId);

        private void OnReadyStartClicked()
        {
            bool isHost = NetworkManager.Singleton.IsHost;

            // Host's button doubles as Start Game once the whole lobby is ready.
            if (isHost && _lobby.AreAllPlayersReady())
                _lobby.StartGame();
            else
                _lobby.ToggleReadyLocal();
        }

        private void SetActiveTab(HeroRole role)
        {
            foreach (var btn in _heroButtons)
                btn.gameObject.SetActive(btn.Role == role);

            for (int i = 0; i < roleTabButtons.Length && i < HeroCatalogSO.TabOrder.Length; i++)
            {
                var img = roleTabButtons[i].GetComponent<Image>();
                if (img != null)
                    img.color = HeroCatalogSO.TabOrder[i] == role ? tabActiveColor : tabInactiveColor;
            }
        }

        // ---------------------------------------------------------------- Repaint

        private void Repaint()
        {
            ulong localId = NetworkManager.Singleton.LocalClientId;
            bool localFound = _lobby.TryGetState(localId, out LobbyPlayerState localState);

            RepaintTopBar(localId);
            RepaintRoster(localId, localFound, localState);
            RepaintActionButton(localFound, localState);
        }

        private void RepaintTopBar(ulong localId)
        {
            for (int slot = 0; slot < playerSlots.Length; slot++)
            {
                if (_lobby.TryGetStateAtSlot(slot, out LobbyPlayerState state))
                    playerSlots[slot].Bind(state, heroCatalog.GetHero(state.SelectedHeroId),
                                           state.ClientId == localId);
                else
                    playerSlots[slot].SetEmpty();
            }
        }

        private void RepaintRoster(ulong localId, bool localFound, in LobbyPlayerState localState)
        {
            // Once ready, your pick is locked — the whole grid goes non-interactable.
            bool canPick = localFound && !localState.IsReady;

            foreach (var btn in _heroButtons)
            {
                bool takenByOther = _lobby.IsHeroTakenByOther(btn.HeroId, localId);
                bool mine = localFound && localState.SelectedHeroId == btn.HeroId;
                btn.SetState(takenByOther, mine, canPick);
            }
        }

        private void RepaintActionButton(bool localFound, in LobbyPlayerState localState)
        {
            bool isHost = NetworkManager.Singleton.IsHost;
            bool hasHero = localFound && localState.SelectedHeroId != HeroCatalogSO.NoSelection;
            bool allReady = _lobby.AreAllPlayersReady();

            if (isHost && allReady)
            {
                readyStartLabel.text = "START GAME";
                readyStartButton.interactable = true;
                SetHint($"All {_lobby.PlayerCount} player(s) ready.");
            }
            else if (localFound && localState.IsReady)
            {
                readyStartLabel.text = "UNREADY";
                readyStartButton.interactable = true;
                SetHint(isHost ? "Waiting for everyone to ready up..." : "Waiting for host to start...");
            }
            else
            {
                readyStartLabel.text = "READY";
                readyStartButton.interactable = hasHero; // server enforces this too
                SetHint(hasHero ? "" : "Select a hero from the roster below.");
            }
        }

        private void SetHint(string msg)
        {
            if (hintText != null) hintText.text = msg;
        }
    }
}
