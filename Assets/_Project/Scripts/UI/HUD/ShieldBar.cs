using UnityEngine;
using UnityEngine.UI;

namespace CBuilding.UI
{
    /// <summary>
    /// GS-16 — thin flat shield bar directly under the health bar. Matte #00F3FF.
    /// Snaps instantly on change — shields feel binary/mechanical, in contrast to
    /// the chunked organic depletion of health.
    ///
    /// NOTE: no gameplay shield system exists yet. The widget is wired and ready;
    /// PlayerHUDController drives it to 0 until a shield stat/NetworkVariable lands.
    /// </summary>
    public class ShieldBar : MonoBehaviour
    {
        [SerializeField] private Image fill;

        private void Awake() => fill.color = UIPalette.Shield;

        public void SetNormalized(float value) => fill.fillAmount = Mathf.Clamp01(value);
    }
}
