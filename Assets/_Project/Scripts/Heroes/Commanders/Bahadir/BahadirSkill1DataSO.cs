using CBuilding.Abilities;
using CBuilding.Abilities.Delivery;
using UnityEngine;

namespace CBuilding.Heroes.Bahadir
{
    /// <summary>
    /// Skill1: two-form weapon. A normal press fires whichever form is currently active
    /// (Form0 = penetrating "0", Form1 = single-target "1"); a press within
    /// <see cref="doubleTapWindow"/> of the previous one toggles form + triggers the
    /// mount-ride instead of firing (GS-9.4 bespoke, tracked outside this doc). Form1 also
    /// carries the "spyware-chain" bonus: every currently Spyware-marked enemy takes the
    /// same Damage+Slow, applied directly (no delivery — the targets are already known).
    /// </summary>
    [CreateAssetMenu(menuName = "CBuilding/Abilities/Heroes/Bahadir/Skill1", fileName = "Ability_Bahadir_Skill1")]
    public class BahadirSkill1DataSO : AbilityDataSO
    {
        [Header("Composable forms (Delivery + Effects + TeamFilter, built in the Inspector)")]
        [Tooltip("CA_Bahadir_Form0 — Projectile(pierce), Stun + Glitch, TeamFilter = Enemies.")]
        public ComposedAbilitySO form0Ability;
        [Tooltip("CA_Bahadir_Form1 — Projectile(single), Damage + Slow, TeamFilter = Enemies.")]
        public ComposedAbilitySO form1Ability;

        [Header("Form switch (bespoke — GS-9.4)")]
        [Tooltip("A second press within this window toggles form + mount-ride instead of firing.")]
        [Min(0.05f)] public float doubleTapWindow = 0.35f;
        [Tooltip("Placeholder speed multiplier for the mount-ride burst until the real bespoke movement lands.")]
        [Min(1f)] public float mountRideSpeedMultiplier = 1.6f;
        [Min(0f)] public float mountRideDuration = 1.5f;

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
