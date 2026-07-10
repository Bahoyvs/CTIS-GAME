using CBuilding.Abilities;
using CBuilding.Abilities.Delivery;
using CBuilding.StatusEffects;
using UnityEngine;

namespace CBuilding.Heroes.Bahadir
{
    /// <summary>
    /// Ultimate: Bahadır channels and roots himself (hard-CC-only-cancel — only external
    /// hard CC interrupts, matching the Channel mode's built-in AbilityController behaviour).
    /// On a clean completion, a "pre-infected zone" opens around the cast point: any enemy
    /// that spawns inside it within the window gets Spyware-marked the instant it appears —
    /// a spawn hook re-using the composable Spyware Effect SO directly, no delivery, since
    /// the target (the freshly spawned enemy) is handed to us by the hook itself.
    /// </summary>
    [CreateAssetMenu(menuName = "CBuilding/Abilities/Heroes/Bahadir/Ultimate", fileName = "Ability_Bahadir_Ultimate")]
    public class BahadirUltimateDataSO : AbilityDataSO
    {
        [Header("Self root (data-driven — controlFlags = Root)")]
        public EffectDataSO selfRootEffect;

        [Header("Spawn-hack window (bespoke — no real spawn-zone system yet, see EnemySpawnHooks)")]
        [Tooltip("Wraps Fx_Spyware — applied directly to each enemy that spawns in-window.")]
        public ApplyStatusEffectSO spawnHackMarkEffect;
        [Min(0f)] public float spawnHackWindowSeconds = 6f;
        [Tooltip("Spawns further than this from the cast point are not infected (approximates a 'zone').")]
        [Min(0.5f)] public float infectionRadius = 8f;

        public override AbilityRuntime CreateRuntime() => new BahadirUltimateRuntime();

        protected override void OnValidate()
        {
            base.OnValidate();
            mode = AbilityMode.Channel;
        }
    }
}
