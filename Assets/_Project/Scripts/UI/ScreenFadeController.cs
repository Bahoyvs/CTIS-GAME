using System.Collections;
using UnityEngine;

namespace CBuilding.UI
{
    /// <summary>
    /// Global fullscreen fade-to-black (floor transitions, future death/finale cinematics).
    ///
    /// PURELY CLIENT-SIDE VISUAL — no NetworkBehaviour. Networked systems (e.g.
    /// FloorTransitionZone) broadcast a ClientRpc and call the static RequestFadeOut/
    /// RequestFadeIn from it; the scene instance listens via a static event, so callers
    /// never need a scene reference (same decoupling as SectionManager.OnSectionChanged).
    ///
    /// SETUP: Canvas (Screen Space - Overlay, Sort Order 100) → fullscreen black Image
    /// (stretch anchors) → CanvasGroup on the same object → attach this script there.
    /// Alpha starts at 0. Keep it OUT of the gameplay HUD canvas so HUD rebuilds
    /// never touch it.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class ScreenFadeController : MonoBehaviour
    {
        private static event System.Action<float, bool> FadeRequested; // (duration, toBlack)

        /// <summary>Fade to black over <paramref name="duration"/> seconds. Safe with no instance alive.</summary>
        public static void RequestFadeOut(float duration) => FadeRequested?.Invoke(duration, true);

        /// <summary>Fade back to gameplay over <paramref name="duration"/> seconds.</summary>
        public static void RequestFadeIn(float duration) => FadeRequested?.Invoke(duration, false);

        private CanvasGroup _group;
        private Coroutine _running;

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;
        }

        private void OnEnable() => FadeRequested += HandleFadeRequested;
        private void OnDisable() => FadeRequested -= HandleFadeRequested;

        private void HandleFadeRequested(float duration, bool toBlack)
        {
            if (_running != null) StopCoroutine(_running); // newest request wins
            _running = StartCoroutine(FadeRoutine(toBlack ? 1f : 0f, duration));
        }

        private IEnumerator FadeRoutine(float target, float duration)
        {
            // Block clicks as soon as we start going dark; unblock only when fully clear.
            if (target > 0f) _group.blocksRaycasts = true;

            float start = _group.alpha;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                // unscaledDeltaTime: fade must run even if a future pause sets timeScale=0.
                elapsed += Time.unscaledDeltaTime;
                _group.alpha = Mathf.Lerp(start, target, duration <= 0f ? 1f : elapsed / duration);
                yield return null;
            }

            _group.alpha = target;
            if (target <= 0f) _group.blocksRaycasts = false;
            _running = null;
        }
    }
}
