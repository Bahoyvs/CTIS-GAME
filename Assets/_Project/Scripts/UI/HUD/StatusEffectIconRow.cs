using CBuilding.StatusEffects;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.UI
{
    /// <summary>
    /// GS-16 — horizontal row of square status icons under the shield bar.
    /// Binds to StatusEffectController.SyncedEffects (server-written NetworkList)
    /// and rebuilds only when that list changes.
    ///
    /// Setup: HorizontalLayoutGroup on this object; assign a pool of
    /// pre-instantiated StatusEffectIconWidget children (inactive by default)
    /// and the EffectIconCatalog asset.
    /// </summary>
    public class StatusEffectIconRow : MonoBehaviour
    {
        [SerializeField] private EffectIconCatalog catalog;
        [SerializeField] private StatusEffectIconWidget[] widgetPool;

        private StatusEffectController bound;

        public void Bind(StatusEffectController status)
        {
            Unbind();
            if (status == null) return;

            bound = status;
            bound.SyncedEffects.OnListChanged += OnEffectsChanged;
            Redraw();
        }

        public void Unbind()
        {
            if (bound != null)
            {
                bound.SyncedEffects.OnListChanged -= OnEffectsChanged;
                bound = null;
            }
            foreach (var w in widgetPool) w.Hide();
        }

        private void OnEffectsChanged(NetworkListEvent<StatusEffectController.ActiveEffectSync> _) => Redraw();

        private void Redraw()
        {
            int shown = 0;
            if (bound != null)
            {
                foreach (var sync in bound.SyncedEffects)
                {
                    if (shown >= widgetPool.Length) break;
                    widgetPool[shown].Show(sync, catalog.GetByHash(sync.EffectHash));
                    shown++;
                }
            }
            for (int i = shown; i < widgetPool.Length; i++)
                widgetPool[i].Hide();
        }

        private void OnDestroy() => Unbind();
    }
}
