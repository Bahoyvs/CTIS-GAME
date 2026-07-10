using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace CBuilding.UI
{
    /// <summary>
    /// GS-16 — player health bar: sharp-edged segmented blocks in flat #FF003C.
    /// Depletes segment-by-segment (chunked), never smoothly.
    ///
    /// Setup: assign N child Images (left-to-right). Each segment:
    ///   Image Type = Filled, Fill Method = Horizontal, Origin = Left,
    ///   plain white square sprite. A dark background Image sits behind the row.
    ///
    /// Event-driven: SetNormalized is only called from BaseHero.OnHealthChanged.
    /// The chunk coroutine is presentation animation kicked off by that event —
    /// no state is polled per frame.
    /// </summary>
    public class SegmentedHealthBar : MonoBehaviour
    {
        [SerializeField] private Image[] segments;
        [Tooltip("Delay between each segment popping during chunked depletion.")]
        [SerializeField] private float chunkStepInterval = 0.045f;

        private float displayedUnits; // current visual fill, in segment units (0..segments.Length)
        private Coroutine animRoutine;

        private void Awake()
        {
            foreach (var s in segments) s.color = UIPalette.Health;
            displayedUnits = segments.Length;
            ApplyFill(displayedUnits);
        }

        /// <param name="value">Health normalized 0..1.</param>
        /// <param name="instant">True for initial bind — snaps without animation.</param>
        public void SetNormalized(float value, bool instant = false)
        {
            float targetUnits = Mathf.Clamp01(value) * segments.Length;

            if (animRoutine != null) StopCoroutine(animRoutine);

            if (instant || !isActiveAndEnabled)
            {
                displayedUnits = targetUnits;
                ApplyFill(targetUnits);
                return;
            }

            animRoutine = StartCoroutine(ChunkRoutine(targetUnits));
        }

        private IEnumerator ChunkRoutine(float targetUnits)
        {
            var wait = new WaitForSeconds(chunkStepInterval);

            while (!Mathf.Approximately(displayedUnits, targetUnits))
            {
                // Step exactly one whole segment per tick — the "chunk" feel.
                displayedUnits = displayedUnits > targetUnits
                    ? Mathf.Max(targetUnits, Mathf.Ceil(displayedUnits - 1f))
                    : Mathf.Min(targetUnits, Mathf.Floor(displayedUnits + 1f));

                ApplyFill(displayedUnits);
                yield return wait;
            }
            animRoutine = null;
        }

        private void ApplyFill(float units)
        {
            for (int i = 0; i < segments.Length; i++)
                segments[i].fillAmount = Mathf.Clamp01(units - i); // boundary segment gets the partial fill
        }
    }
}
