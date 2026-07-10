using System.Collections;
using CBuilding.Enemies;
using CBuilding.StatusEffects;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace CBuilding.UI
{
    /// <summary>
    /// GS-16 — world-space micro-canvas above an enemy's head (child of the enemy
    /// prefab, next to a UIBillboard). Binds to BaseEnemy.NetHealth and the enemy's
    /// StatusEffectController synced summary. Event-driven; smooth depletion is a
    /// short lerp kicked off per damage event (unlike the player's chunked bar).
    ///
    /// Setup (prefab, under a World Space Canvas, GraphicRaycaster removed):
    ///   - "HealthBG":    dark strip
    ///   - "HealthFill":  flat red strip, Type = Filled, Horizontal, Origin = Left
    ///   - "ShieldFill":  thinner neon blue strip stacked on top (future enemy shields)
    ///   - "DebuffSlot":  small square Image glued to the far-LEFT edge of the bar
    ///   - "FrozenFrame": minimalist ice-crystal frame around the bar (inactive)
    /// </summary>
    public class EnemyWorldUI : MonoBehaviour
    {
        [Header("Bars")]
        [SerializeField] private Image healthFill;
        [SerializeField] private Image shieldFill;    // optional — no enemy shield system yet
        [SerializeField] private GameObject shieldLayer;

        [Header("Active Effect Slot (far left)")]
        [SerializeField] private Image debuffIcon;
        [SerializeField] private GameObject frozenFrame;
        [SerializeField] private EffectIconCatalog catalog;

        [Header("Feel")]
        [Tooltip("Seconds for the smooth deplete lerp after a damage event.")]
        [SerializeField] private float depleteTime = 0.15f;

        private BaseEnemy enemy;
        private StatusEffectController status;
        private Coroutine healthRoutine;

        private void Awake()
        {
            healthFill.color = UIPalette.Health;
            if (shieldFill != null) shieldFill.color = UIPalette.Shield;
            if (shieldLayer != null) shieldLayer.SetActive(false); // until enemy shields exist

            enemy = GetComponentInParent<BaseEnemy>();
            status = GetComponentInParent<StatusEffectController>();
        }

        private void OnEnable()
        {
            if (enemy != null)
            {
                enemy.NetHealth.OnValueChanged += OnHealthChanged;
                healthFill.fillAmount = SafeRatio(enemy.NetHealth.Value, enemy.MaxHealth);
            }

            if (status != null)
            {
                status.SyncedEffects.OnListChanged += OnEffectsChanged;
                status.OnControlFlagsChanged += OnControlFlagsChanged;
                RedrawDebuffSlot();
                OnControlFlagsChanged(ControlFlags.None, status.Flags);
            }
            else
            {
                debuffIcon.gameObject.SetActive(false);
                frozenFrame.SetActive(false);
            }
        }

        private void OnDisable()
        {
            if (enemy != null)
                enemy.NetHealth.OnValueChanged -= OnHealthChanged;

            if (status != null)
            {
                status.SyncedEffects.OnListChanged -= OnEffectsChanged;
                status.OnControlFlagsChanged -= OnControlFlagsChanged;
            }
        }

        // ---- Event handlers ----

        private void OnHealthChanged(float _, float now)
        {
            float target = SafeRatio(now, enemy.MaxHealth);
            if (healthRoutine != null) StopCoroutine(healthRoutine);
            if (!isActiveAndEnabled) { healthFill.fillAmount = target; return; }
            healthRoutine = StartCoroutine(SmoothDeplete(target));
        }

        private IEnumerator SmoothDeplete(float target)
        {
            float start = healthFill.fillAmount;
            float t = 0f;
            while (t < depleteTime)
            {
                t += Time.deltaTime;
                healthFill.fillAmount = Mathf.Lerp(start, target, t / depleteTime);
                yield return null;
            }
            healthFill.fillAmount = target;
            healthRoutine = null;
        }

        private void OnEffectsChanged(NetworkListEvent<StatusEffectController.ActiveEffectSync> _) =>
            RedrawDebuffSlot();

        /// <summary>Shows the most dominant active debuff: highest stacks wins, ties -> newest.</summary>
        private void RedrawDebuffSlot()
        {
            EffectDataSO dominant = null;
            int bestStacks = -1;

            foreach (var sync in status.SyncedEffects)
            {
                var data = catalog != null ? catalog.GetByHash(sync.EffectHash) : null;
                if (data == null || !data.isDebuff || data.icon == null) continue;
                if (sync.Stacks >= bestStacks)
                {
                    bestStacks = sync.Stacks;
                    dominant = data;
                }
            }

            debuffIcon.gameObject.SetActive(dominant != null);
            if (dominant != null) debuffIcon.sprite = dominant.icon;
        }

        private void OnControlFlagsChanged(ControlFlags _, ControlFlags current) =>
            frozenFrame.SetActive((current & ControlFlags.Freeze) != 0);

        private static float SafeRatio(float value, float max) => max > 0f ? value / max : 0f;
    }
}
