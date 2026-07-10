using CBuilding.Abilities;
using CBuilding.Data;
using CBuilding.Heroes;
using UnityEngine;
using UnityEngine.UI;

namespace CBuilding.UI
{
    /// <summary>
    /// GS-16 — one circular teammate widget: pixelated avatar, class-colored frame,
    /// health/shield semi-arcs, ult + voice LEDs, death blackout.
    ///
    /// Setup (prefab):
    ///   - "Frame":      circle outline Image (class color at bind)
    ///   - "Avatar":     pixelated head/helmet silhouette Image
    ///   - "HealthArc":  ring Image, Filled, Radial 360, Origin = Bottom,
    ///                   Clockwise = OFF (left half). Fill capped at 0.5 in code.
    ///   - "ShieldArc":  same but Clockwise = ON (right half)
    ///   - "DeadOverlay":blackout circle + crossed-out red line child (inactive)
    ///   - "UltLed":     tiny circle Image (top, right of avatar)
    ///   - "VoiceLed":   tiny circle Image (bottom, right of avatar)
    /// </summary>
    public class TeammateWidget : MonoBehaviour
    {
        [Header("Core")]
        [SerializeField] private Image frame;
        [SerializeField] private Image avatar;

        [Header("Status Arcs")]
        [SerializeField] private Image healthArc; // left half
        [SerializeField] private Image shieldArc; // right half — 0 until a shield system lands

        [Header("States")]
        [SerializeField] private GameObject deadOverlay; // blackout + red cross line

        [Header("LEDs")]
        [SerializeField] private Image ultLed;
        [SerializeField] private Image voiceLed;

        private BaseHero bound;
        private AbilityController boundAbilities;

        public bool IsBound => bound != null;
        public BaseHero Bound => bound;

        public void Bind(BaseHero ally)
        {
            Unbind();
            bound = ally;

            HeroRole role = ally.Stats != null && ally.Stats.BaseStats != null
                ? ally.Stats.BaseStats.Role
                : HeroRole.DPS;
            frame.color = UIPalette.GetClassColor(role);
            healthArc.color = UIPalette.Health;
            shieldArc.color = UIPalette.Shield;
            shieldArc.fillAmount = 0f;

            bound.OnHealthChanged += OnHealthChanged;
            bound.OnDied += OnDied;
            OnHealthChanged(bound.CurrentHealth, bound.Stats.GetStat(StatType.MaxHealth));
            deadOverlay.SetActive(!bound.IsAlive);

            // Ult LED: replicated-to-everyone readiness flag on AbilityController (GS-16).
            boundAbilities = ally.GetComponent<AbilityController>();
            if (boundAbilities != null)
            {
                boundAbilities.NetUltimateReady.OnValueChanged += OnUltReadyChanged;
                OnUltReadyChanged(false, boundAbilities.NetUltimateReady.Value);
            }
            else
            {
                ultLed.color = UIPalette.LedOff;
            }

            SetSpeaking(false); // voice system hook — call SetSpeaking from Vivox later

            gameObject.SetActive(true);
        }

        public void Unbind()
        {
            if (bound != null)
            {
                bound.OnHealthChanged -= OnHealthChanged;
                bound.OnDied -= OnDied;
                bound = null;
            }
            if (boundAbilities != null)
            {
                boundAbilities.NetUltimateReady.OnValueChanged -= OnUltReadyChanged;
                boundAbilities = null;
            }
            gameObject.SetActive(false);
        }

        /// <summary>Voice comms hook (bottom LED). Wire to Vivox speaking events later.</summary>
        public void SetSpeaking(bool speaking) =>
            voiceLed.color = speaking ? UIPalette.LedReady : UIPalette.LedOff;

        // ---- Event handlers ----

        private void OnHealthChanged(float current, float max)
        {
            // Each arc occupies half the circle: fill = 0.5 * normalized value.
            healthArc.fillAmount = 0.5f * (max > 0f ? Mathf.Clamp01(current / max) : 0f);

            // Revive support: a heal past 0 clears the blackout.
            if (current > 0f && deadOverlay.activeSelf) deadOverlay.SetActive(false);
        }

        private void OnDied(BaseHero _) => deadOverlay.SetActive(true);

        private void OnUltReadyChanged(bool _, bool ready) =>
            ultLed.color = ready ? UIPalette.LedReady : UIPalette.LedOff;

        private void OnDestroy() => Unbind();
    }
}
