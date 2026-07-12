using UnityEngine;
using CBuilding.Core;

namespace CBuilding.Enemies.Roster
{
    /// <summary>
    /// The Greedy / The Contented paired gimmick:
    ///   Greedy (BlockAbove, 700)    — fully blocks any single hit GREATER than the threshold
    ///   Contented (BlockBelow, 550) — fully blocks any single hit SMALLER than the threshold
    /// Only the 551–700 band damages both, forcing mixed hit sizes or smart target-splitting.
    /// ALWAYS co-spawn them (PairedCoSpawn on Greedy) — alone, one just reads as unkillable
    /// to half the kit. IDamageModifier at priority 240: runs after marks/multipliers so
    /// the band is judged on the REAL post-modifier amount.
    /// </summary>
    public class DamageBandBlocker : MonoBehaviour, IDamageModifier
    {
        public enum BlockMode
        {
            BlockAbove, // The Greedy: too big a bite — refuses it entirely.
            BlockBelow  // The Contented: too small to bother with.
        }

        [SerializeField] private BlockMode mode = BlockMode.BlockAbove;

        [Tooltip("Greedy: hits STRICTLY greater are blocked. Contented: hits STRICTLY smaller are blocked.")]
        [Min(1f)] [SerializeField] private float threshold = 700f;

        public int Priority => 240;

        private DamageModifierPipeline _pipeline;

        private void Awake() => _pipeline = GetComponent<DamageModifierPipeline>();
        private void OnEnable() => _pipeline?.Register(this);
        private void OnDisable() => _pipeline?.Unregister(this);

        public float Modify(in DamageInfo info, float currentAmount)
        {
            if (info.IsHealing || currentAmount <= 0f) return currentAmount;

            bool blocked = mode == BlockMode.BlockAbove
                ? currentAmount > threshold
                : currentAmount < threshold;

            return blocked ? 0f : currentAmount;
        }
    }
}
