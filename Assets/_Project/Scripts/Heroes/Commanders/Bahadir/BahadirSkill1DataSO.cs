using CBuilding.Abilities;
using CBuilding.Abilities.Delivery;
using UnityEngine;

namespace CBuilding.Heroes.Bahadir
{
    /// <summary>
    /// Skill1: two-form weapon. Each press fires whichever form is currently active
    /// (Form0 = penetrating "0", Form1 = single-target "1") and then flips to the other
    /// form for next time — plain auto-alternation, no toggle input, no timing window, no
    /// mount-ride (that system was removed as over-complicated). Form1 also carries the
    /// "spyware-chain" bonus: every currently Spyware-marked enemy takes the same
    /// Damage+Slow, applied directly (no delivery — the targets are already known).
    /// </summary>
    [CreateAssetMenu(menuName = "CBuilding/Abilities/Heroes/Bahadir/Skill1", fileName = "Ability_Bahadir_Skill1")]
    public class BahadirSkill1DataSO : AbilityDataSO
    {
        [Header("Composable forms (Delivery + Effects + TeamFilter, built in the Inspector)")]
        [Tooltip("CA_Bahadir_Form0 — Projectile(pierce), Stun + Glitch, TeamFilter = Enemies.")]
        public ComposedAbilitySO form0Ability;
        [Tooltip("CA_Bahadir_Form1 — Projectile(single), Damage + Slow, TeamFilter = Enemies.")]
        public ComposedAbilitySO form1Ability;

        [Header("Form1 spyware-chain bonus (direct Effect apply — no delivery, GS-9 §1)")]
        public DamageEffectSO chainDamageEffect;
        public ApplyStatusEffectSO chainSlowEffect;

        public override AbilityRuntime CreateRuntime() => new BahadirSkill1Runtime();

        protected override void OnValidate()
        {
            base.OnValidate();
            mode = AbilityMode.Instant;
        }
    }
}
