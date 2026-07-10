using UnityEngine;

namespace CBuilding.Abilities.Delivery
{
    /// <summary>
    /// THE standard ability asset (GS-9): delivery (how) × effects (what) × filter (who).
    /// Create via: Assets → Create → CBuilding → Abilities → Composed Ability.
    ///
    /// Recipes from the 8 MVP kits:
    ///   Kerem Feature  = Arc delivery            + [Damage→Enemies]
    ///   Kerem S1       = Projectile(explosion)   + [Damage→Enemies]
    ///   Gobluna S1     = Projectile(x3, pierce)  + [Damage→Enemies, Heal→Allies]
    ///   Gobluna S2     = Zone(persistent)        + [Damage→Enemies, Status(burn)→Enemies]
    ///   TL S2          = Projectile(x8, radial)  + [Damage→Enemies, Status(stun)→Enemies]
    ///   Ok S1 burst    = Area                    + [Status(blind)→Enemies]
    ///   Ironworks S1   = Zone                    + [Status(damage reduction)→Allies]
    ///   Bahadır S2     = Projectile or Nearest   + [Status(Spyware mark)→Enemies]
    ///   Kerem S2       = Line                    + [Displacement(Pull)→Enemies, Status(stun)→Enemies]
    ///
    /// Sequenced/stateful mechanics (Bahadır's form switch, AP's mount, Ug's tap-vs-hold,
    /// transformations, tethers, turrets) still subclass AbilityDataSO with a bespoke
    /// AbilityRuntime — and those runtimes can EXECUTE deliveries/effects from code, so
    /// even bespoke kits reuse this layer instead of re-implementing target acquisition.
    /// </summary>
    [CreateAssetMenu(menuName = "CBuilding/Abilities/Composed Ability", fileName = "Ability_")]
    public class ComposedAbilitySO : AbilityDataSO
    {
        [Header("Delivery (how targets are acquired)")]
        public AbilityDeliverySO delivery;

        [Header("Team filter (who the delivery may acquire)")]
        public TeamFilter teamFilter = TeamFilter.Enemies;

        [Header("Effects (what lands on each acquired target — each self-filters via appliesTo)")]
        public AbilityEffectSO[] effects;

        public override AbilityRuntime CreateRuntime() => new Runtime();

        /// <summary>
        /// Server-side helper so BESPOKE runtimes can fire this ability's delivery at an
        /// arbitrary point (channel ticks, trail drops, echo casts).
        /// </summary>
        public void ExecuteDelivery(AbilityController controller, Vector3 aimPoint)
        {
            if (delivery == null)
            {
                Debug.LogWarning($"[{name}] No delivery assigned.");
                return;
            }
            delivery.Execute(new AbilityCastContext(controller, this, aimPoint));
        }

        private class Runtime : AbilityRuntime
        {
            public override void Execute()
            {
                var data = (ComposedAbilitySO)Data;
                data.ExecuteDelivery(Controller, Controller.CurrentAimPoint);
            }
        }
    }
}
