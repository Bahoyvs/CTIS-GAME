using CBuilding.Abilities;
using CBuilding.Heroes;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Heroes.Bahadir
{
    /// <summary>
    /// Commander archetype special modification (design doc: Feature grants a per-
    /// archetype twist on top of its base kit — Gladiator = basic attack, Commander =
    /// Roll). For Bahadır: ROLL IS THE TRIGGER for Feature (Infiltration). There is no
    /// separate Feature input bound in the Input Actions asset — pressing Roll both
    /// dashes (HeroController's normal, untouched Roll movement) AND requests Feature
    /// activation on the same button press.
    ///
    /// Feature's own cooldown (14s, AbilityController's CooldownManager) is what gates
    /// re-entering Stealth — TryActivate silently no-ops server-side while it's on
    /// cooldown, so mashing Roll never spam-refreshes Stealth, but the dash itself always
    /// happens regardless (this component never touches HeroController's roll movement).
    ///
    /// PREFAB SETUP: add next to HeroController + AbilityController on Hero_Bahadır
    /// (root). Discovered automatically via GetComponent&lt;IRollBehaviour&gt;() in
    /// HeroController.Awake() — no Inspector wiring needed.
    /// </summary>
    [RequireComponent(typeof(HeroController))]
    public class BahadirRollController : NetworkBehaviour, IRollBehaviour
    {
        private AbilityController _abilities;

        private void Awake()
        {
            _abilities = GetComponent<AbilityController>();
        }

        // ---- IRollBehaviour (OWNER) ----

        public void OnRollStart(HeroController hero, Vector3 rollDirection)
        {
            if (!IsOwner || _abilities == null) return;
            if (!_abilities.HasSlotAssigned(AbilitySlot.Feature)) return;

            // TryActivate sends the owner->server RPC; ServerTryActivate re-checks the
            // cooldown/silence/channeling gates for real — this call is a request, not
            // a guarantee. AimPoint doesn't matter here (BahadirFeatureDataSO's buff/
            // pass-through deliveries both use castRange 0 = centered on the caster).
            _abilities.TryActivate(AbilitySlot.Feature, hero.AimPoint);
        }

        public void OnRollEnd(HeroController hero)
        {
            // Activation only needs the press edge — Feature's own 4s channel (ticking
            // independently via BahadirFeatureRuntime) covers the rest of the dash and beyond.
        }
    }
}
