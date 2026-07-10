using UnityEngine;

namespace CBuilding.Abilities
{
    /// <summary>
    /// GS-9.1 — configuration half of the config/logic split.
    /// One asset per ability. Subclass and override <see cref="CreateRuntime"/> to bind
    /// the logic half (an <see cref="AbilityRuntime"/> subclass).
    /// Assets live in _Project/Data/Heroes/... next to the other SO instances.
    /// </summary>
    public abstract class AbilityDataSO : ScriptableObject
    {
        [Header("Identity")]
        public string abilityId;
        public string displayName;
        [TextArea] public string description;
        public Sprite icon;

        [Header("Activation (GS-9.4)")]
        public AbilityMode mode = AbilityMode.Instant;
        [Min(0f)] public float cooldown = 5f;

        [Header("Channel (mode = Channel)")]
        [Min(0f)] public float channelDuration = 0f;

        [Header("Charges (mode = ChargeBased)")]
        [Min(1)] public int maxCharges = 1;

        [Header("Gating")]
        [Tooltip("Passives usually can't be silenced; actives can.")]
        public bool blockedBySilence = true;

        /// <summary>Stable hash for RPC payloads / sync.</summary>
        public int AbilityHash => string.IsNullOrEmpty(abilityId)
            ? name.GetHashCode()
            : abilityId.GetHashCode();

        /// <summary>Create the server-side logic instance for this ability.</summary>
        public abstract AbilityRuntime CreateRuntime();

        protected virtual void OnValidate()
        {
            if (string.IsNullOrEmpty(abilityId)) abilityId = name;
        }
    }
}
