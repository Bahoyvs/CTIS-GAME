using CBuilding.Heroes;
using UnityEngine;

namespace CBuilding.UI
{
    /// <summary>
    /// GS-16 — bottom-left panel: 3 vertically stacked TeammateWidgets.
    /// Assigns every non-owned hero to a free widget as they spawn (via the
    /// BaseHero spawn registry); releases it on despawn. Purely event-driven.
    /// </summary>
    public class TeammatePanelController : MonoBehaviour
    {
        [SerializeField] private TeammateWidget[] widgets = new TeammateWidget[3];

        private void OnEnable()
        {
            BaseHero.OnHeroSpawned += OnHeroSpawned;
            BaseHero.OnHeroDespawned += OnHeroDespawned;

            foreach (var w in widgets) w.Unbind(); // start hidden
            foreach (var hero in BaseHero.ActiveHeroes) OnHeroSpawned(hero);
        }

        private void OnDisable()
        {
            BaseHero.OnHeroSpawned -= OnHeroSpawned;
            BaseHero.OnHeroDespawned -= OnHeroDespawned;
            foreach (var w in widgets) w.Unbind();
        }

        private void OnHeroSpawned(BaseHero hero)
        {
            if (hero.IsOwner) return; // the local hero lives in the main HUD

            foreach (var w in widgets)
            {
                if (w.IsBound) continue;
                w.Bind(hero);
                return;
            }
        }

        private void OnHeroDespawned(BaseHero hero)
        {
            foreach (var w in widgets)
                if (w.Bound == hero)
                    w.Unbind();
        }
    }
}
