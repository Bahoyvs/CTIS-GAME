using UnityEngine;

namespace CBuilding.StatusEffects
{
    /// <summary>
    /// GS-5.3 — data asset for one catalog effect (Stun, Root, Freeze, Slow, Blind,
    /// Silence, DoT variants, Isolate, SpywareMark/MarkOfGuilt...).
    /// The default runtime (<see cref="GenericStatusEffect"/>) is fully data-driven:
    /// control flags + move-speed multiplier + DoT + damage-taken/heal multipliers.
    /// Only genuinely bespoke behaviour (e.g. Isolate's sensory presentation)
    /// should subclass this SO and override <see cref="CreateRuntime"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "CBuilding/Status Effects/Effect", fileName = "Effect_")]
    public class EffectDataSO : ScriptableObject
    {
        [Header("Identity")]
        public string effectId;
        public string displayName;
        public Sprite icon;
        [Tooltip("Effects marked as debuffs can be filtered by cleanses later.")]
        public bool isDebuff = true;

        [Header("Lifetime")]
        [Min(0f)] public float duration = 3f;
        [Tooltip("0 = no ticking (pure state effect).")]
        [Min(0f)] public float tickInterval = 0f;

        [Header("Stacking (GS-5.2)")]
        public StackingPolicy stackingPolicy = StackingPolicy.Refresh;
        [Min(1)] public int maxStacks = 1;

        [Header("Control (aggregated on the controller)")]
        public ControlFlags controlFlags = ControlFlags.None;

        [Header("Movement")]
        [Tooltip("1 = unchanged. 0.6 = Slow to 60% speed. Multiplied across active effects.")]
        [Range(0f, 2f)] public float moveSpeedMultiplier = 1f;

        [Header("Damage over time (per tick, requires tickInterval > 0)")]
        [Min(0f)] public float damagePerTick = 0f;

        [Header("Damage pipeline hooks (GS-5.4)")]
        [Tooltip("Multiplier applied to incoming DAMAGE while active (SpywareMark/MarkOfGuilt ≈ 1.25). 1 = none. Scales with stacks under StackIntensity.")]
        [Min(0f)] public float incomingDamageMultiplier = 1f;
        [Tooltip("Multiplier applied to incoming HEALING while active (Anti-heal < 1). 1 = none. Scales with stacks under StackIntensity.")]
        [Min(0f)] public float incomingHealMultiplier = 1f;

        /// <summary>Stable hash for network sync of active-effect lists.</summary>
        public int EffectHash => string.IsNullOrEmpty(effectId)
            ? name.GetHashCode()
            : effectId.GetHashCode();

        public virtual IStatusEffect CreateRuntime()
        {
            return new GenericStatusEffect(this);
        }

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(effectId)) effectId = name;
            if (damagePerTick > 0f && tickInterval <= 0f)
            {
                Debug.LogWarning($"[{name}] damagePerTick set but tickInterval is 0 — DoT will never tick.", this);
            }
        }
    }
}
