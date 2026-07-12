using CBuilding.Abilities;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Heroes.Gobluna
{
    /// <summary>
    /// Gobluna's Feature trigger, mirroring BahadirRollController: ROLL requests Feature
    /// (Leap &amp; Heal Aura) on the same press. The dash itself always happens; Feature's
    /// server-side gates (cooldown, leapable-ally check in GoblunaFeatureRuntime.CanActivate)
    /// decide whether the leap actually fires. AimPoint matters here — unlike Bahadır,
    /// Gobluna's Feature uses it to pick WHICH ally to leap to.
    ///
    /// While the leap is active, Fx_GoblunaLeapRoot suppresses normal roll movement
    /// (HeroController checks CanMove), so the leap and the dash never fight.
    ///
    /// PREFAB SETUP: next to HeroController + AbilityController on Hero_Gobluna (root).
    /// Discovered via GetComponent&lt;IRollBehaviour&gt;() — no Inspector wiring.
    /// </summary>
    [RequireComponent(typeof(HeroController))]
    public class GoblunaRollController : NetworkBehaviour, IRollBehaviour
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

            _abilities.TryActivate(AbilitySlot.Feature, hero.AimPoint);
        }

        public void OnRollEnd(HeroController hero) { }
    }
}
