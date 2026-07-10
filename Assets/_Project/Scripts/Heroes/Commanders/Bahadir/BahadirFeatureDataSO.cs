using CBuilding.Abilities;
using CBuilding.Abilities.Delivery;
using UnityEngine;

namespace CBuilding.Heroes.Bahadir
{
    /// <summary>
    /// Feature (right-click): self stealth+speed buff, and while it's up, every enemy
    /// Bahadır passes through gets stunned. The buff/self part and the pass-through-stun
    /// part are both fully composable (CA_Bahadir_Feature_Buff / CA_Bahadir_Feature_PassStun)
    /// — this data asset is bespoke only because SOMETHING has to repeatedly re-fire the
    /// pass-through delivery for the buff's duration (see BahadirFeatureRuntime).
    /// mode = Channel is reused here purely as a free repeating-tick mechanism: the caster
    /// is NOT actually locked (HeroController doesn't gate movement on IsChanneling), and
    /// only hard CC interrupts it early — same "hard-CC-only-cancel" contract the framework
    /// already gives Channel abilities for free.
    /// </summary>
    [CreateAssetMenu(menuName = "CBuilding/Abilities/Heroes/Bahadir/Feature", fileName = "Ability_Bahadir_Feature")]
    public class BahadirFeatureDataSO : AbilityDataSO
    {
        [Header("Composable payloads (Delivery + Effects + TeamFilter, built in the Inspector)")]
        [Tooltip("Self, instant: CA_Bahadir_Feature_Buff (Stealth + SpeedBuff, TeamFilter = Self).")]
        public ComposedAbilitySO buffAbility;
        [Tooltip("PointArea, re-fired on a tick: CA_Bahadir_Feature_PassStun (Stun, TeamFilter = Enemies).")]
        public ComposedAbilitySO passThroughStunAbility;

        [Header("Pass-through tick (test checklist: low frequency, not per-frame)")]
        [Min(0.05f)] public float passThroughTickInterval = 0.5f;

        public override AbilityRuntime CreateRuntime() => new BahadirFeatureRuntime();

        protected override void OnValidate()
        {
            base.OnValidate();
            mode = AbilityMode.Channel; // buff duration IS the channel duration.
        }
    }
}
