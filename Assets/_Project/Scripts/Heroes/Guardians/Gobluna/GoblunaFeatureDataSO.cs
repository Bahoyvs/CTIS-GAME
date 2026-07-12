using CBuilding.Abilities;
using CBuilding.Abilities.Delivery;
using CBuilding.StatusEffects;
using UnityEngine;

namespace CBuilding.Heroes.Gobluna
{
    /// <summary>
    /// Feature: Leap &amp; Heal Aura. She leaps to an ally and, on arrival, drops a
    /// persistent healing circle (a plain ZoneDelivery ComposedAbilitySO — AbilityZone
    /// already team-filters, so no "EnemyHazardZone for allies" variant is needed).
    ///
    /// The leap itself is the bespoke part: movement is OWNER-authoritative
    /// (ClientNetworkTransform), so the server-side runtime picks the ally, roots her
    /// for the flight window, asks GoblunaHeroController to RPC the owner into the
    /// dash, and drops the zone at the ally's position when the flight time elapses.
    /// </summary>
    [CreateAssetMenu(menuName = "CBuilding/Abilities/Heroes/Gobluna/Feature", fileName = "Ability_Gobluna_Feature")]
    public class GoblunaFeatureDataSO : AbilityDataSO
    {
        [Header("Leap")]
        [Tooltip("Max distance from Gobluna to a leapable ally. No ally in range = cast refused (CanActivate), no cooldown spent.")]
        [Min(1f)] public float leapRange = 10f;
        [Tooltip("Flight time. The heal zone drops when this elapses.")]
        [Min(0.1f)] public float leapDuration = 0.35f;
        [Tooltip("Fx_GoblunaLeapRoot — Root, duration ≈ leapDuration, isDebuff OFF. Stops WASD from fighting the leap on the owner.")]
        public EffectDataSO selfRootEffect;

        [Header("Heal aura (composable — dropped at the ally's feet on arrival)")]
        [Tooltip("CA_Gobluna_Feature_Zone — ZoneDelivery (persistent) + Heal → AlliesAndSelf, TeamFilter = AlliesAndSelf.")]
        public ComposedAbilitySO healZoneAbility;

        public override AbilityRuntime CreateRuntime() => new GoblunaFeatureRuntime();
    }
}
