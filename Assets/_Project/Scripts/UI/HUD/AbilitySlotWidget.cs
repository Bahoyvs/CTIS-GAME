using System.Collections;
using CBuilding.Abilities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CBuilding.UI
{
    /// <summary>
    /// GS-16 — one ability circle: white vector icon on black disc, class-colored
    /// ring, clockwise radial cooldown mask, ready-pulse juice, charge pips/counter.
    ///
    /// Driven exclusively by AbilityBarController routing AbilityController's
    /// OnCooldownUpdated / OnChargesUpdated owner events (GS-9.5 mirror). Refunds
    /// and ReduceAllActive mid-cooldown simply produce a new event, which restarts
    /// the local animation at the corrected remaining time.
    ///
    /// Setup (prefab, back to front):
    ///   - "Ring":         circle outline sprite (colored by class at bind)
    ///   - "Disc":         solid black circle Image
    ///   - "Icon":         Image (sprite auto-pulled from AbilityDataSO.icon)
    ///   - "CooldownMask": circle Image, Type = Filled, Radial 360, Origin = Top,
    ///                     Clockwise = ON. fillAmount 1 -> 0 wipes away clockwise.
    ///   - "PulseRing":    duplicate of Ring, inactive by default
    ///   - "Pips":         up to N tiny squares, bottom-right exterior
    ///   - "Counter":      tiny TMP cyber-font text, bottom-right exterior
    /// </summary>
    public class AbilitySlotWidget : MonoBehaviour
    {
        [Header("Visuals")]
        [SerializeField] private Image ring;
        [SerializeField] private Image icon;
        [SerializeField] private Image cooldownMask;
        [SerializeField] private Image pulseRing;

        [Header("Custom Data (charges/stacks)")]
        [SerializeField] private Image[] stackPips;      // used when count fits the pip row
        [SerializeField] private TMP_Text stackCounter;  // fallback tiny cyber-font counter

        [Header("Juice")]
        [SerializeField] private float pulseDuration = 0.35f;
        [SerializeField] private float pulseScale = 1.55f;

        private Coroutine cooldownRoutine;
        private Coroutine pulseRoutine;
        private bool isCooling;
        private bool showCharges;

        public void Setup(AbilityDataSO data, Color classColor)
        {
            ring.color = classColor;
            icon.color = UIPalette.IconWhite;
            cooldownMask.color = UIPalette.CooldownMask;
            cooldownMask.fillAmount = 0f;
            cooldownMask.enabled = false;
            pulseRing.color = classColor;
            pulseRing.gameObject.SetActive(false);

            if (data != null && data.icon != null) icon.sprite = data.icon;

            // Charges are only meaningful for ChargeBased abilities (Kerem's stacks).
            showCharges = data != null && data.mode == AbilityMode.ChargeBased;
            RedrawStacks(showCharges ? data.maxCharges : 0);

            isCooling = false;
        }

        public void Clear()
        {
            if (cooldownRoutine != null) { StopCoroutine(cooldownRoutine); cooldownRoutine = null; }
            if (pulseRoutine != null) { StopCoroutine(pulseRoutine); pulseRoutine = null; }
            isCooling = false;
        }

        // ================= Cooldown (event-driven) =================

        public void HandleCooldownUpdated(float remaining, float duration)
        {
            if (cooldownRoutine != null) { StopCoroutine(cooldownRoutine); cooldownRoutine = null; }

            if (remaining <= 0f)
            {
                cooldownMask.fillAmount = 0f;
                cooldownMask.enabled = false;
                if (isCooling && isActiveAndEnabled) Pulse(); // cooling -> ready: single subtle pulse
                isCooling = false;
                return;
            }

            isCooling = true;
            if (!isActiveAndEnabled)
            {
                cooldownMask.enabled = true;
                cooldownMask.fillAmount = Mathf.Clamp01(remaining / Mathf.Max(duration, 0.01f));
                return; // can't run coroutines while inactive; next event corrects the fill
            }
            cooldownRoutine = StartCoroutine(CooldownRoutine(remaining, Mathf.Max(duration, 0.01f)));
        }

        // Event-initiated animation: counts the replicated remaining time down locally.
        // No gameplay state is polled — a refund/reduction triggers a fresh event.
        private IEnumerator CooldownRoutine(float remaining, float duration)
        {
            cooldownMask.enabled = true;

            while (remaining > 0f)
            {
                cooldownMask.fillAmount = Mathf.Clamp01(remaining / duration);
                yield return null;
                remaining -= Time.deltaTime;
            }

            cooldownMask.fillAmount = 0f;
            cooldownMask.enabled = false;
            cooldownRoutine = null;
            isCooling = false;
            Pulse();
        }

        private void Pulse()
        {
            if (pulseRoutine != null) StopCoroutine(pulseRoutine);
            pulseRoutine = StartCoroutine(PulseRoutine());
        }

        private IEnumerator PulseRoutine()
        {
            pulseRing.gameObject.SetActive(true);
            var rt = pulseRing.rectTransform;
            Color baseColor = pulseRing.color;

            float t = 0f;
            while (t < pulseDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / pulseDuration);
                rt.localScale = Vector3.one * Mathf.Lerp(1f, pulseScale, k);
                pulseRing.color = new Color(baseColor.r, baseColor.g, baseColor.b, 1f - k);
                yield return null;
            }

            rt.localScale = Vector3.one;
            pulseRing.color = baseColor;
            pulseRing.gameObject.SetActive(false);
            pulseRoutine = null;
        }

        // ================= Charges / stacks =================

        public void HandleChargesUpdated(int charges)
        {
            if (showCharges) RedrawStacks(charges);
        }

        private void RedrawStacks(int count)
        {
            bool usePips = showCharges && count <= stackPips.Length;

            for (int i = 0; i < stackPips.Length; i++)
                stackPips[i].gameObject.SetActive(usePips && i < count);

            bool useCounter = showCharges && !usePips && count > 0;
            stackCounter.gameObject.SetActive(useCounter);
            if (useCounter) stackCounter.text = count.ToString();
        }
    }
}
