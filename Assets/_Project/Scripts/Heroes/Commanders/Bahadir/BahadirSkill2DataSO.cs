using CBuilding.Abilities;
using CBuilding.Abilities.Delivery;
using CBuilding.StatusEffects;
using UnityEngine;

namespace CBuilding.Heroes.Bahadir
{
    /// <summary>
    /// Skill2: charge-based mark shot. Landing the mark and letting it kill the target
    /// ("virus returns") refunds cooldown and opens a short window in which the NEXT
    /// Bahadır-sourced Stun re-applies the mark for free (no charge spent) — the
    /// "zincirleme" (chaining) mechanic. Both direct-apply hooks (§1 of the plan doc) reuse
    /// the composable Effect SO's, they just skip the Delivery/targeting step because the
    /// target is already known (self, or whoever the chained stun just hit).
    /// </summary>
    [CreateAssetMenu(menuName = "CBuilding/Abilities/Heroes/Bahadir/Skill2", fileName = "Ability_Bahadir_Skill2")]
    public class BahadirSkill2DataSO : AbilityDataSO
    {
        [Header("Composable mark shot")]
        [Tooltip("CA_Bahadir_S2_Mark — Projectile(single), ApplyStatus(Spyware), TeamFilter = Enemies.")]
        public ComposedAbilitySO markAbility;

        [Header("Virus-return (bespoke — charge economy)")]
        [Tooltip("Fx_CooldownReduction — instant status, applied to self via StatusEffectController.")]
        public EffectDataSO cooldownReductionEffect;
        [Tooltip("How long after a return-proc the next Bahadır stun still chains a free mark.")]
        [Min(0f)] public float chainWindowSeconds = 3f;

        [Header("Chain re-mark (direct Effect apply — no delivery, no charge cost)")]
        [Tooltip("Wraps Fx_Spyware — applied directly to whoever the chained stun landed on.")]
        public ApplyStatusEffectSO chainMarkEffect;
        [Tooltip("Which EffectDataSO counts as 'a Bahadır stun' for the chain window (assign Fx_Stun).")]
        public EffectDataSO bahadirStunEffect;

        public override AbilityRuntime CreateRuntime() => new BahadirSkill2Runtime();

        protected override void OnValidate()
        {
            base.OnValidate();
            mode = AbilityMode.ChargeBased;
        }
    }
}
