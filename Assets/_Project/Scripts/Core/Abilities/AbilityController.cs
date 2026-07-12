using System;
using System;
using System.Collections.Generic;
using CBuilding.Core;
using CBuilding.Heroes;
using CBuilding.StatusEffects;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Abilities
{
    /// <summary>
    /// GS-9.3 — the single ability pipeline shared by all heroes (and reusable for
    /// boss units). Flow:
    ///   owner input → TryActivate(slot) → server RPC → server validation
    ///   (cooldown/charges/silence/mode) → AbilityRuntime.Execute() →
    ///   activation ClientRpc for VFX/anim.
    /// Contains NO hero-specific branching (GS-9.4): variation comes from
    /// AbilityDataSO.mode and AbilityRuntime subclasses.
    ///
    /// PREFAB SETUP: add next to the concrete BaseHero subclass and assign the six
    /// slot assets below (GS-9.2 contract). HeroController input calls TryActivate.
    /// </summary>
    public class AbilityController : NetworkBehaviour
    {
        [Header("Slot assignment (GS-9.2 contract)")]
        [SerializeField] private AbilityDataSO feature;   // right-click
        [SerializeField] private AbilityDataSO passive;
        [SerializeField] private AbilityDataSO finalPassive;
        [SerializeField] private AbilityDataSO skill1;
        [SerializeField] private AbilityDataSO skill2;
        [SerializeField] private AbilityDataSO ultimate;

        private readonly Dictionary<AbilitySlot, AbilityRuntime> _runtimes = new();
        private readonly CooldownManager _cooldowns = new();

        private StatusEffectController _status;
        private BaseHero _hero;

        // Channel state (one channel at a time per entity)
        private AbilitySlot _channelSlot;
        private float _channelRemaining;
        private bool _isChanneling;

        // Toggle state
        private readonly HashSet<AbilitySlot> _toggledOn = new();

        /// <summary>Server-side cooldown manager — the shared ReduceAllActive API (GS-9.5, GS-13, GS-14).</summary>
        public CooldownManager Cooldowns => _cooldowns;

        /// <summary>
        /// Server-only, valid during runtime hooks (Execute/ChannelTick): the caster's aim
        /// point at activation time (owner's mouse-on-ground-plane point, sent with the RPC).
        /// Delivery logic (TargetedAbilitySO) clamps this to its own cast range — the
        /// client's click is a SUGGESTION, same rule as HeroController.CastSynergyServerRpc.
        /// </summary>
        public Vector3 CurrentAimPoint { get; private set; }

        /// <summary>Raised everywhere an activation is announced (owner + observers) for VFX/anim/UI.</summary>
        public event Action<AbilitySlot> OnAbilityActivated;

        /// <summary>Owner-side cooldown mirror for HUD (GS-16): (slot, remaining, duration).</summary>
        public event Action<AbilitySlot, float, float> OnCooldownUpdated;

        /// <summary>Owner-side charge mirror for HUD stack pips (GS-16): (slot, charges).</summary>
        public event Action<AbilitySlot, int> OnChargesUpdated;

        // GS-16: Ultimate readiness replicated to EVERYONE (teammate panel LEDs read this;
        // per-slot cooldowns stay owner-only to keep bandwidth minimal).
        private readonly NetworkVariable<bool> _netUltimateReady = new(
            true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Any-peer: is this hero's Ultimate off cooldown? Subscribe for LED updates.</summary>
        public NetworkVariable<bool> NetUltimateReady => _netUltimateReady;

        private void Awake()
        {
            _status = GetComponent<StatusEffectController>();
            _hero = GetComponent<BaseHero>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                BindSlot(AbilitySlot.Feature, feature);
                BindSlot(AbilitySlot.Passive, passive);
                BindSlot(AbilitySlot.FinalPassive, finalPassive);
                BindSlot(AbilitySlot.Skill1, skill1);
                BindSlot(AbilitySlot.Skill2, skill2);
                BindSlot(AbilitySlot.Ultimate, ultimate);

                _cooldowns.OnCooldownChanged += HandleServerCooldownChanged;

                // GS-13.3 — stun pauses cooldown clocks (relevant for boss units;
                // harmless for heroes, whose stuns simply block activation anyway).
                if (_status != null)
                {
                    _status.OnControlFlagsChanged += HandleControlFlagsChanged;
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                _cooldowns.OnCooldownChanged -= HandleServerCooldownChanged;
                if (_status != null)
                {
                    _status.OnControlFlagsChanged -= HandleControlFlagsChanged;
                }
            }
        }

        private void BindSlot(AbilitySlot slot, AbilityDataSO data)
        {
            if (data != null) ServerInitializeSlot(slot, data);
        }

        /// <summary>
        /// Server-only. Binds a data asset to a slot. Also usable for dynamic kits
        /// (boss units, GS-14 Defender tools).
        /// </summary>
        public void ServerInitializeSlot(AbilitySlot slot, AbilityDataSO data)
        {
            if (!IsServer || data == null) return;

            var runtime = data.CreateRuntime();
            runtime.Initialize(this, data, slot);
            _runtimes[slot] = runtime;
            _cooldowns.RegisterSlot(slot, data.mode == AbilityMode.ChargeBased ? data.maxCharges : 1);
        }

        public AbilityDataSO GetSlotData(AbilitySlot slot) =>
            _runtimes.TryGetValue(slot, out var r) ? r.Data : null;

        /// <summary>
        /// Valid on ANY peer (reads the serialized prefab fields, not the server-built
        /// runtimes) — lets owner-side input code check assignment before sending an RPC.
        /// </summary>
        public bool HasSlotAssigned(AbilitySlot slot) => GetSerializedSlot(slot) != null;

        /// <summary>
        /// Any-peer read of the assigned data asset (icon, display name, maxCharges) —
        /// the HUD builds slot visuals from this without waiting for server runtimes.
        /// </summary>
        public AbilityDataSO GetAssignedData(AbilitySlot slot) => GetSerializedSlot(slot);

        private AbilityDataSO GetSerializedSlot(AbilitySlot slot) => slot switch
        {
            AbilitySlot.Feature => feature,
            AbilitySlot.Passive => passive,
            AbilitySlot.FinalPassive => finalPassive,
            AbilitySlot.Skill1 => skill1,
            AbilitySlot.Skill2 => skill2,
            AbilitySlot.Ultimate => ultimate,
            _ => null
        };

        // ---- Client entry point ----

        /// <summary>Owner-side input entry point without aim data (self-cast abilities).</summary>
        public void TryActivate(AbilitySlot slot) => TryActivate(slot, transform.position);

        /// <summary>Owner-side input entry point (HeroController passes its AimPoint). Validation happens on the server.</summary>
        public void TryActivate(AbilitySlot slot, Vector3 aimPoint)
        {
            if (!IsOwner) return;
            RequestActivateRpc(slot, aimPoint);
        }

        [Rpc(SendTo.Server, RequireOwnership = true)]
        private void RequestActivateRpc(AbilitySlot slot, Vector3 aimPoint)
        {
            ServerTryActivate(slot, aimPoint);
        }

        // ---- Server logic ----

        /// <summary>Server-only, no aim data (boss AI self-casts).</summary>
        public void ServerTryActivate(AbilitySlot slot) => ServerTryActivate(slot, transform.position);

        /// <summary>Server-only. Also callable directly by server systems (boss AI, GS-8 modules).</summary>
        public void ServerTryActivate(AbilitySlot slot, Vector3 aimPoint)
        {
            if (!IsServer) return;
            if (!_runtimes.TryGetValue(slot, out var runtime)) return;

            var data = runtime.Data;
            string casterName = _hero != null ? _hero.DisplayName : gameObject.name;

            // Toggle-off is always allowed (even while silenced, ending a toggle is safe).
            if (data.mode == AbilityMode.Toggle && _toggledOn.Contains(slot))
            {
                _toggledOn.Remove(slot);
                runtime.ToggleEnd();
                _cooldowns.Commit(slot, data.cooldown); // cooldown starts on toggle-off
                CombatLogManager.LogAbilityToggle(casterName, data.displayName, false, transform.position);
                return;
            }

            // Gating (GS-5 integration).
            if (data.blockedBySilence && _status != null && !_status.CanUseAbilities)
            {
                CombatLogManager.LogAbilityBlocked(casterName, data.displayName, "Silenced");
                return;
            }

            if (!_cooldowns.IsReady(slot))
            {
                CombatLogManager.LogAbilityBlocked(casterName, data.displayName, "Cooldown Active");
                return;
            }

            if (_isChanneling)
            {
                CombatLogManager.LogAbilityBlocked(casterName, data.displayName, "Already Channeling");
                return; // one channel at a time; also blocks casts mid-channel
            }

            CurrentAimPoint = aimPoint; // available to CanActivate/Execute (range checks, delivery)

            if (!runtime.CanActivate())
            {
                CombatLogManager.LogAbilityBlocked(casterName, data.displayName, "Cannot Activate");
                return;
            }

            switch (data.mode)
            {
                case AbilityMode.Instant:
                case AbilityMode.ChargeBased:
                    runtime.Execute();
                    _cooldowns.Commit(slot, data.cooldown);
                    CombatLogManager.LogAbilityActivated(casterName, data.displayName, data.mode.ToString(), aimPoint);
                    break;

                case AbilityMode.Toggle:
                    _toggledOn.Add(slot);
                    runtime.Execute(); // toggle ON; no cooldown yet
                    CombatLogManager.LogAbilityToggle(casterName, data.displayName, true, transform.position);
                    break;

                case AbilityMode.Channel:
                    _isChanneling = true;
                    _channelSlot = slot;
                    _channelRemaining = data.channelDuration;
                    runtime.Execute(); // channel start
                    CombatLogManager.LogAbilityChannelStart(casterName, data.displayName, data.channelDuration, aimPoint);
                    break;
            }

            AnnounceActivationRpc(slot);
        }

        /// <summary>
        /// Server-only. Ends the active channel; called by the tick when time runs out,
        /// by AbilityRuntime.EndChannelEarly (AP mechanic), or by interrupts (stun).
        /// </summary>
        public void ServerEndChannel(AbilitySlot slot, bool completed, float cooldownRefund = 0f)
        {
            if (!IsServer || !_isChanneling || _channelSlot != slot) return;

            _isChanneling = false;
            var runtime = _runtimes[slot];
            string casterName = _hero != null ? _hero.DisplayName : gameObject.name;

            CombatLogManager.LogAbilityChannelEnd(casterName, runtime.Data.displayName, completed, transform.position);

            runtime.ChannelEnd(completed);
            _cooldowns.Commit(slot, runtime.Data.cooldown);
            if (cooldownRefund > 0f)
            {
                _cooldowns.Refund(slot, cooldownRefund);
            }
        }

        private void Update()
        {
            if (!IsServer) return;

            _cooldowns.Tick(Time.deltaTime);

            if (_isChanneling)
            {
                // Stun/freeze interrupts the channel (GS-5).
                if (_status != null && _status.IsStunned)
                {
                    ServerEndChannel(_channelSlot, completed: false);
                    return;
                }

                var runtime = _runtimes[_channelSlot];
                runtime.ChannelTick(Time.deltaTime);

                _channelRemaining -= Time.deltaTime;
                if (_channelRemaining <= 0f)
                {
                    ServerEndChannel(_channelSlot, completed: true);
                }
            }
        }

        private void HandleControlFlagsChanged(ControlFlags previous, ControlFlags current)
        {
            // GS-13.3: freeze cooldown clocks while stunned.
            bool stunned = (current & (ControlFlags.Stun | ControlFlags.Freeze)) != 0;
            _cooldowns.SetFrozen(stunned);
        }

        // ---- Sync to clients ----

        [Rpc(SendTo.Everyone)]
        private void AnnounceActivationRpc(AbilitySlot slot)
        {
            OnAbilityActivated?.Invoke(slot);
        }

        private void HandleServerCooldownChanged(AbilitySlot slot, float remaining, float duration)
        {
            // GS-16: teammate LEDs — replicated to everyone, delta-synced only on change.
            if (slot == AbilitySlot.Ultimate)
                _netUltimateReady.Value = remaining <= 0f;

            int charges = _cooldowns.GetCharges(slot);

            // Host-local owner gets the event directly; remote owners via RPC.
            if (IsOwner)
            {
                OnCooldownUpdated?.Invoke(slot, remaining, duration);
                OnChargesUpdated?.Invoke(slot, charges);
            }
            else
            {
                SyncCooldownRpc(slot, remaining, duration, charges);
            }
        }

        [Rpc(SendTo.Owner)]
        private void SyncCooldownRpc(AbilitySlot slot, float remaining, float duration, int charges)
        {
            OnCooldownUpdated?.Invoke(slot, remaining, duration);
            OnChargesUpdated?.Invoke(slot, charges);
        }
    }
}
