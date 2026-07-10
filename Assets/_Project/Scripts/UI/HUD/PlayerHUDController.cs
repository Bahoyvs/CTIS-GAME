using CBuilding.Abilities;
using CBuilding.Data;
using CBuilding.Heroes;
using CBuilding.StatusEffects;
using UnityEngine;

namespace CBuilding.UI
{
    /// <summary>
    /// GS-16 — root binder for the local player's HUD (Screen Space - Camera canvas).
    /// Waits for the OWNED BaseHero to spawn (BaseHero.OnHeroSpawned registry), then
    /// wires every widget to its events. Zero Update() polling.
    /// </summary>
    public class PlayerHUDController : MonoBehaviour
    {
        [Header("Top Left")]
        [SerializeField] private SegmentedHealthBar healthBar;
        [SerializeField] private ShieldBar shieldBar; // wired, driven to 0 until a shield system lands
        [SerializeField] private StatusEffectIconRow statusRow;

        [Header("Bottom Center")]
        [SerializeField] private AbilityBarController abilityBar;

        private BaseHero bound;

        private void OnEnable()
        {
            BaseHero.OnHeroSpawned += TryBind;
            BaseHero.OnHeroDespawned += OnHeroDespawned;

            // Hero may already exist (HUD enabled after spawn).
            foreach (var hero in BaseHero.ActiveHeroes) TryBind(hero);
        }

        private void OnDisable()
        {
            BaseHero.OnHeroSpawned -= TryBind;
            BaseHero.OnHeroDespawned -= OnHeroDespawned;
            Unbind();
        }

        private void TryBind(BaseHero hero)
        {
            if (bound != null || !hero.IsOwner) return;
            bound = hero;

            // ---- Vitals: BaseHero already exposes the (current, max) event. ----
            bound.OnHealthChanged += OnHealthChanged;
            healthBar.SetNormalized(SafeRatio(bound.CurrentHealth,
                bound.Stats.GetStat(StatType.MaxHealth)), instant: true);

            if (shieldBar != null) shieldBar.SetNormalized(0f); // no shield system yet

            // ---- Status effects (GS-5 synced summary). ----
            statusRow.Bind(bound.GetComponent<StatusEffectController>());

            // ---- Abilities (GS-9 owner-side cooldown mirror). ----
            HeroRole role = bound.Stats != null && bound.Stats.BaseStats != null
                ? bound.Stats.BaseStats.Role
                : HeroRole.DPS;
            abilityBar.Bind(bound.GetComponent<AbilityController>(), role);
        }

        private void OnHeroDespawned(BaseHero hero)
        {
            if (hero == bound) Unbind();
        }

        private void Unbind()
        {
            if (bound == null) return;

            bound.OnHealthChanged -= OnHealthChanged;
            statusRow.Unbind();
            abilityBar.Unbind();
            bound = null;
        }

        private void OnHealthChanged(float current, float max) =>
            healthBar.SetNormalized(SafeRatio(current, max));

        private static float SafeRatio(float value, float max) => max > 0f ? value / max : 0f;
    }
}
