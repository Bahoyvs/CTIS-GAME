using System;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Enemies.Spawning
{
    /// <summary>
    /// Networked "which environmental events are active right now" authority
    /// (NightPhase, Sandstorm, DebrisShower, Vacuum). Server writes, everyone reads —
    /// the SpawnDirector reads it for weight modifiers, and client VFX/lighting systems
    /// can subscribe to <see cref="OnEventsChanged"/> to fade in storms / night.
    ///
    /// SETUP: one instance per gameplay scene, next to NetworkGameManager/SectionManager.
    /// </summary>
    public class EnvironmentalEventManager : NetworkBehaviour
    {
        public static EnvironmentalEventManager Instance { get; private set; }

        /// <summary>(previous, current) — fired on every peer.</summary>
        public static event Action<EnvironmentalEventType, EnvironmentalEventType> OnEventsChanged;

        private readonly NetworkVariable<int> _netActiveEvents = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Currently active events. Valid on every peer; None if no instance yet.</summary>
        public static EnvironmentalEventType ActiveEvents =>
            Instance != null ? (EnvironmentalEventType)Instance._netActiveEvents.Value : EnvironmentalEventType.None;

        public static bool IsActive(EnvironmentalEventType evt) => (ActiveEvents & evt) != 0;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[EnvironmentalEventManager] Duplicate instance destroyed.", this);
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            _netActiveEvents.OnValueChanged += HandleChanged;
        }

        public override void OnNetworkDespawn()
        {
            _netActiveEvents.OnValueChanged -= HandleChanged;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            if (Instance == this) Instance = null;
        }

        private void HandleChanged(int previous, int current) =>
            OnEventsChanged?.Invoke((EnvironmentalEventType)previous, (EnvironmentalEventType)current);

        // ---- Server API ----

        /// <summary>Server-only. Turns one event on/off, preserving the others.</summary>
        public void ServerSetEvent(EnvironmentalEventType evt, bool active)
        {
            if (!IsServer) return;
            int flags = _netActiveEvents.Value;
            _netActiveEvents.Value = active ? flags | (int)evt : flags & ~(int)evt;
        }

        /// <summary>Server-only. Section transitions: wipe everything (storm ends with the desert).</summary>
        public void ServerClearAll()
        {
            if (!IsServer) return;
            _netActiveEvents.Value = 0;
        }
    }
}
