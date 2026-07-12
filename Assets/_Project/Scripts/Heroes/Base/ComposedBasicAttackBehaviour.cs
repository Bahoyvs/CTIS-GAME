using CBuilding.Abilities;
using CBuilding.Abilities.Delivery;
using CBuilding.Core;
using UnityEngine;

namespace CBuilding.Heroes
{
    /// <summary>
    /// GS-17 §2 + §7.1 — the data-driven basic attack track for the 6 non-Gladiator
    /// heroes. Wraps up to three ComposedAbilitySO assets (one per Section) and swaps
    /// the active one on SectionManager.OnSectionChanged. Null tiers fall back to the
    /// previous Section's asset, so a hero whose archetype doesn't change at S2 only
    /// needs one asset wired.
    ///
    /// PREFAB SETUP: add next to HeroController + AbilityController, assign the
    /// Section 1 asset (2/3 optional). HeroController discovers this via
    /// GetComponent&lt;IBasicAttackBehaviour&gt;().
    /// </summary>
    public class ComposedBasicAttackBehaviour : MonoBehaviour, IBasicAttackBehaviour, IHoldableBasicAttack
    {
        [Header("Input Ayarları")]
        public bool autoFire = false; // Inspector'a Auto-fire kutucuğu ekliyoruz

        [Header("Per-Section basic attack (GS-17 §7.1)")]
        [SerializeField] private ComposedAbilitySO section1Ability;
        [Tooltip("null = falls back to Section 1's asset.")]
        [SerializeField] private ComposedAbilitySO section2Ability;
        [Tooltip("null = falls back to Section 2's (then Section 1's) asset.")]
        [SerializeField] private ComposedAbilitySO section3Ability;

        private ComposedAbilitySO _active;
        private AbilityController _abilities;

        private void Awake()
        {
            _abilities = GetComponent<AbilityController>();
            if (_abilities == null)
                Debug.LogError("[ComposedBasicAttackBehaviour] Requires an AbilityController sibling (delivery cast context).", this);
        }

        private void OnEnable()
        {
            _active = ResolveForSection(SectionManager.CurrentSection);
            SectionManager.OnSectionChanged += HandleSectionChanged;
        }

        private void OnDisable() => SectionManager.OnSectionChanged -= HandleSectionChanged;

        private void HandleSectionChanged(int sectionIndex)
        {
            _active = ResolveForSection(sectionIndex);
        }

        private ComposedAbilitySO ResolveForSection(int sectionIndex) => sectionIndex switch
        {
            1 => section1Ability,
            2 => section2Ability != null ? section2Ability : section1Ability,
            3 => section3Ability != null ? section3Ability
                : (section2Ability != null ? section2Ability : section1Ability),
            _ => _active != null ? _active : section1Ability
        };

        /// <summary>SERVER-only (called from HeroController's validated attack path).</summary>
        public void Fire(HeroController hero, Vector3 aimPoint)
        {
            if (_active == null || _abilities == null) return;
            // Same ExecuteDelivery pipeline as Skill1/2/Ultimate — crit/label-bonus
            // modifiers ride along for free (the GS-9.4 standing rule).
            _active.ExecuteDelivery(_abilities, aimPoint);
        }

        // ---- IHoldableBasicAttack (OWNER side) ----

        public bool HoldEnabled(HeroController hero)
        {
            return autoFire;
        }

        public void OnHoldBegin(HeroController hero, Vector3 aimPoint)
        {
            if (autoFire) hero.TryPerformBasicAttack();
        }

        public void OnHoldUpdate(HeroController hero, Vector3 currentWorldPoint)
        {
            if (autoFire) hero.TryPerformBasicAttack();
        }

        public void OnHoldRelease(HeroController hero)
        {
        }
    }
}
