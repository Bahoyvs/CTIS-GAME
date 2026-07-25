using CBuilding.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CBuilding.UI
{
    /// <summary>
    /// HUD widget for FloorTransitionZone (the inter-floor elevator/airlock).
    ///
    /// ONE instance in the HUD serves EVERY zone in the scene: it subscribes to the
    /// static FloorTransitionZone.OnLocalZoneChanged hook (same decoupled pattern as
    /// CombatLogDisplay ← CombatLogManager). No singleton, no scene references —
    /// gameplay code never knows this class exists.
    ///
    /// Payload contract: a ZONE instance while the local hero stands inside one
    /// (re-fired on every replicated count/ready/consumed change), NULL when they leave.
    ///
    /// SETUP: HUD Canvas → "TransitionPromptWidget" (CanvasGroup + this script)
    ///        → child TextMeshProUGUI ("PromptText", centered, auto-size or ~28pt).
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class TransitionPromptUI : MonoBehaviour
    {
        [Header("UI References")]
        [Tooltip("İki satırlık durum yazısı: sayaç + talimat.")]
        [SerializeField] private TextMeshProUGUI promptText;

        [Tooltip("Boş bırakılırsa bu objenin üzerindeki CanvasGroup kullanılır.")]
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Colors")]
        [SerializeField] private Color readyColor = new(0.35f, 1f, 0.45f);
        [SerializeField] private Color waitingColor = new(1f, 0.85f, 0.3f);

        // Resolved from the Input System at startup ("F" today; auto-follows any rebind,
        // so a future keybinding menu never has to touch this class).
        private string _interactKeyLabel = "F";

        private void Awake()
        {
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            canvasGroup.blocksRaycasts = false; // HUD overlay — never eat clicks
            canvasGroup.interactable = false;
            ResolveInteractKeyLabel();
            Hide();
        }

        private void ResolveInteractKeyLabel()
        {
            // Throwaway instance purely to read the current binding's display string —
            // gameplay input stays owned by HeroController/FloorTransitionZone.
            var input = new InputSystem_Actions();
            string label = input.Player.Interact.GetBindingDisplayString(
                UnityEngine.InputSystem.InputBinding.MaskByGroup("Keyboard&Mouse"));
            input.Dispose();

            if (!string.IsNullOrWhiteSpace(label))
                _interactKeyLabel = label.ToUpperInvariant();
        }

        private void OnEnable() => FloorTransitionZone.OnLocalZoneChanged += HandleZoneChanged;
        private void OnDisable() => FloorTransitionZone.OnLocalZoneChanged -= HandleZoneChanged;

        private void HandleZoneChanged(FloorTransitionZone zone)
        {
            // NULL = local hero left the zone. Consumed = elevator already fired.
            if (zone == null || zone.IsConsumed)
            {
                Hide();
                return;
            }

            string hex = ColorUtility.ToHtmlStringRGB(zone.IsReady ? readyColor : waitingColor);
            string instruction = zone.IsReady
                ? $"HOLD [{_interactKeyLabel}] TO TRANSITION"
                : "WAITING FOR TEAM...";

            promptText.text =
                $"<color=#{hex}>{zone.AliveInside} / {zone.AliveTotal} READY</color>\n{instruction}";
            canvasGroup.alpha = 1f;
        }

        private void Hide() => canvasGroup.alpha = 0f;
    }
}
