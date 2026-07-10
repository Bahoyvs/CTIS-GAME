using System.Collections;
using CBuilding.StatusEffects;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace CBuilding.UI
{
    /// <summary>
    /// GS-16 — one minimalist square status icon with a very thin white circular
    /// line depleting counter-clockwise around it (diegetic duration — no numbers).
    ///
    /// Setup (prefab):
    ///   - Root: this component + square black background Image
    ///   - "Icon": Image (sprite comes from EffectDataSO.icon)
    ///   - "Ring": thin ring sprite Image, Type = Filled, Radial 360,
    ///             Origin = Top, Clockwise = OFF  -> depletes counter-clockwise.
    /// </summary>
    public class StatusEffectIconWidget : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private Image ring;

        public int EffectHash { get; private set; }

        private Coroutine ringRoutine;

        /// <summary>Show / refresh this effect. Called only from SyncedEffects list events.</summary>
        public void Show(StatusEffectController.ActiveEffectSync sync, EffectDataSO data)
        {
            EffectHash = sync.EffectHash;
            icon.sprite = data != null ? data.icon : null;
            icon.color = UIPalette.IconWhite;
            ring.color = UIPalette.IconWhite;
            gameObject.SetActive(true);

            // Total duration for the radial: the authored duration. StackDuration
            // re-applies can exceed it; the fill simply clamps at full until inside range.
            float totalDuration = data != null ? Mathf.Max(0.01f, data.duration) : 1f;

            if (ringRoutine != null) StopCoroutine(ringRoutine);
            ringRoutine = StartCoroutine(RingRoutine(sync.ExpiryServerTime, totalDuration));
        }

        public void Hide()
        {
            if (ringRoutine != null) { StopCoroutine(ringRoutine); ringRoutine = null; }
            gameObject.SetActive(false);
        }

        // Event-initiated presentation animation; expiry is replicated once as server
        // time, so the countdown runs locally — no per-frame network/game-state reads.
        private IEnumerator RingRoutine(float expiryServerTime, float totalDuration)
        {
            while (true)
            {
                float now = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening
                    ? (float)NetworkManager.Singleton.ServerTime.Time
                    : Time.time;

                float remaining = expiryServerTime - now;
                if (remaining <= 0f) break;

                ring.fillAmount = Mathf.Clamp01(remaining / totalDuration);
                yield return null;
            }
            ringRoutine = null;
            Hide(); // server expiry will confirm removal via the list event
        }
    }
}
