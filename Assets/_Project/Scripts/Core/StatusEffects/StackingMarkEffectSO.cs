using System;
using CBuilding.Core;
using UnityEngine;

namespace CBuilding.StatusEffects
{
    /// <summary>
    /// GS-17 §6.3 — Core StackingMarkEffect: an N-max-stack debuff with per-stack
    /// damage amplification and a "reached max stacks" event. Kerem's 4-segment mark
    /// and Bahadır's Spyware are the same shape; this generalizes it so it's written once.
    ///
    /// Rec #4 — source lock is a FLAG, not hardcoded: default ON (only damage from the
    /// hero who applied the mark is amplified — simpler, contained, no team-balance
    /// ripple). Untick to make the mark a shared team debuff later without a rewrite;
    /// the flag is the balance knob.
    ///
    /// ASSET SETUP: stackingPolicy MUST be StackIntensity; set maxStacks (Kerem = 4).
    /// Leave incomingDamageMultiplier at 1 — this runtime does its own pipeline math.
    /// </summary>
    [CreateAssetMenu(menuName = "CBuilding/Status Effects/Stacking Mark", fileName = "Fx_StackingMark")]
    public class StackingMarkEffectSO : EffectDataSO
    {
        [Header("Stacking Mark (GS-17)")]
        [Tooltip("Damage amp per filled stack. 0.06 = +6% per segment (4 stacks = +24%). Additive across stacks, not compounding.")]
        [Min(0f)] public float perStackDamageBonus = 0.06f;
        [Tooltip("Rec #4: ON = only the marker's own damage is amplified (Kerem default). OFF = any hero's damage benefits (shared team debuff).")]
        public bool sourceLocked = true;

        public override IStatusEffect CreateRuntime() => new StackingMarkStatus(this);

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(effectId)) effectId = name;
            if (stackingPolicy != StackingPolicy.StackIntensity)
                Debug.LogWarning($"[{name}] StackingMarkEffectSO requires StackingPolicy.StackIntensity to accumulate segments.", this);
            if (!Mathf.Approximately(incomingDamageMultiplier, 1f))
                Debug.LogWarning($"[{name}] Leave incomingDamageMultiplier at 1 — StackingMarkStatus applies its own per-stack amp.", this);
        }
    }

    /// <summary>
    /// Public (same pattern as SpywareMarkStatus) so hero runtimes can query it via
    /// StatusEffectController.GetActiveEffectOfType&lt;StackingMarkStatus&gt;() —
    /// Kerem's telekinesis grab filters on IsFullyStacked + IsFrom(kerem).
    /// </summary>
    public class StackingMarkStatus : IStatusEffect, IDamageModifier
    {
        /// <summary>Server-only. (source who applied the mark, marked target). Fired once per fill-up.</summary>
        public static event Action<GameObject, GameObject> OnMaxStacksReached;

        private readonly StackingMarkEffectSO _data;
        private DamageModifierPipeline _pipeline;
        private GameObject _source;
        private GameObject _target;
        private int _stacks = 1;
        private bool _maxAnnounced;

        public StackingMarkStatus(StackingMarkEffectSO data) => _data = data;

        public EffectDataSO Data => _data;
        public int Stacks => _stacks;
        public bool IsFullyStacked => _stacks >= _data.maxStacks;
        public bool IsFrom(GameObject source) => !_data.sourceLocked || _source == source;

        public void OnApply(StatusEffectContext context)
        {
            _source = context.Source;
            _target = context.Target;
            _pipeline = context.Target.GetComponent<DamageModifierPipeline>();
            _pipeline?.Register(this);
            AnnounceIfMax();
        }

        public void OnTick(StatusEffectContext context, float deltaTime) { }

        public void OnExpire(StatusEffectContext context)
        {
            _pipeline?.Unregister(this);
            _pipeline = null;
        }

        public void OnStacksChanged(StatusEffectContext context, int stacks)
        {
            _stacks = Mathf.Max(1, stacks);
            AnnounceIfMax();
        }

        private void AnnounceIfMax()
        {
            if (_maxAnnounced || !IsFullyStacked) return;
            _maxAnnounced = true;
            OnMaxStacksReached?.Invoke(_source, _target);
        }

        // ---- IDamageModifier (GS-5.4) ----

        public int Priority => 100; // multiplicative band

        public float Modify(in DamageInfo info, float currentAmount)
        {
            if (info.IsHealing) return currentAmount;
            // Rec #4 — the source-lock flag, applied at the only place it matters.
            if (_data.sourceLocked && info.Instigator != _source) return currentAmount;
            return currentAmount * (1f + _data.perStackDamageBonus * _stacks);
        }
    }
}
