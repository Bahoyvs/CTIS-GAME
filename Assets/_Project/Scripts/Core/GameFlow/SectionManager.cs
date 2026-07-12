using System;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Core
{
    /// <summary>
    /// GS-17 / GameFlow — minimal networked "which Section (1/2/3) is the run in" authority.
    /// One instance per scene (next to NetworkGameManager). Server writes, everyone reads.
    ///
    /// Consumers today:
    ///   - ComposedBasicAttackBehaviour (GS-17 §7.1) swaps its active basic-attack SO.
    ///   - APBasicAttackController (GS-17 §6.4) gates chain-shot target counts.
    /// Both subscribe to <see cref="OnSectionChanged"/> — a plain static event so hero
    /// prefab components don't need a scene reference to this object.
    /// </summary>
    public class SectionManager : NetworkBehaviour
    {
        public static SectionManager Instance { get; private set; }

        /// <summary>Fired on EVERY peer whenever the section changes. Payload: 1, 2 or 3.</summary>
        public static event Action<int> OnSectionChanged;

        private readonly NetworkVariable<int> _netSection = new(
            1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Current section (1-based). Valid on every peer.</summary>
        public static int CurrentSection => Instance != null ? Instance._netSection.Value : 1;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[SectionManager] Duplicate instance destroyed.", this);
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            _netSection.OnValueChanged += HandleSectionChanged;
            // Late joiners: replay the current value so subscribers initialize correctly.
            OnSectionChanged?.Invoke(_netSection.Value);
        }

        public override void OnNetworkDespawn()
        {
            _netSection.OnValueChanged -= HandleSectionChanged;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            if (Instance == this) Instance = null;
        }

        private void HandleSectionChanged(int previous, int current)
        {
            OnSectionChanged?.Invoke(current);
        }

        // ---- Server API ----

        /// <summary>Server-only. Clamped to 1..3.</summary>
        public void ServerSetSection(int section)
        {
            if (!IsServer) return;
            _netSection.Value = Mathf.Clamp(section, 1, 3);
        }

        /// <summary>Server-only convenience for GameFlow transitions.</summary>
        public void ServerAdvanceSection() => ServerSetSection(_netSection.Value + 1);
    }
}
