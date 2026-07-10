using System;
using System.Collections.Generic;
using CBuilding.Core;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.StatusEffects
{
    /// <summary>
    /// GS-5.2 — the single status-effect component for every damage-receiving entity
    /// (hero, enemy, boss unit). Server-authoritative: effects are applied, ticked and
    /// expired on the server; clients receive a synced summary (control flags + active
    /// effect list) for input gating, VFX and UI.
    ///
    /// PREFAB SETUP: add next to BaseHero / BaseEnemy. DamageModifierPipeline is
    /// auto-added and shared with TakeDamage/ServerHeal (GS-5.4).
    ///
    /// Consumers:
    ///  - Movement reads MoveSpeedMultiplier and CanMove.
    ///  - AbilityController (GS-9) reads CanUseAbilities.
    ///  - BossGroupController (GS-13.3) subscribes to OnControlFlagsChanged to freeze
    ///    boss cooldowns while stunned.
    /// </summary>
    [RequireComponent(typeof(DamageModifierPipeline))]
    public class StatusEffectController : NetworkBehaviour
    {
        /// <summary>Client-visible summary of one active effect (UI icons, timers).</summary>
        public struct ActiveEffectSync : INetworkSerializable, IEquatable<ActiveEffectSync>
        {
            public int EffectHash;
            public float ExpiryServerTime; // NetworkManager.ServerTime based
            public int Stacks;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref EffectHash);
                serializer.SerializeValue(ref ExpiryServerTime);
                serializer.SerializeValue(ref Stacks);
            }

            public bool Equals(ActiveEffectSync other) =>
                EffectHash == other.EffectHash &&
                Mathf.Approximately(ExpiryServerTime, other.ExpiryServerTime) &&
                Stacks == other.Stacks;
        }

        private class ActiveEffect
        {
            public EffectDataSO Data;
            public IStatusEffect Runtime;
            public StatusEffectContext Context;
            public float Remaining;
            public float TickTimer;
            public int Stacks = 1;
        }

        private readonly List<ActiveEffect> _active = new();

        private readonly NetworkVariable<ControlFlags> _controlFlags = new(
            ControlFlags.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkList<ActiveEffectSync> _syncedEffects = new(
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        // ---- Public query surface (valid on server and clients) ----

        public ControlFlags Flags => _controlFlags.Value;
        public bool CanMove => (Flags & (ControlFlags.Stun | ControlFlags.Root | ControlFlags.Freeze | ControlFlags.Isolate)) == 0;
        public bool CanUseAbilities => (Flags & (ControlFlags.Stun | ControlFlags.Silence | ControlFlags.Freeze | ControlFlags.Isolate)) == 0;
        public bool IsStunned => (Flags & (ControlFlags.Stun | ControlFlags.Freeze)) != 0;
        public bool IsStealthed => (Flags & ControlFlags.Stealth) != 0;

        /// <summary>Server-computed product of all active moveSpeedMultipliers.</summary>
        public float MoveSpeedMultiplier { get; private set; } = 1f;

        public NetworkList<ActiveEffectSync> SyncedEffects => _syncedEffects;

        /// <summary>(previous, current) — raised on server and clients.</summary>
        public event Action<ControlFlags, ControlFlags> OnControlFlagsChanged;

        /// <summary>Server-only. Raised on apply/expire with the effect data.</summary>
        public event Action<EffectDataSO> OnEffectApplied;
        public event Action<EffectDataSO> OnEffectExpired;

        public override void OnNetworkSpawn()
        {
            _controlFlags.OnValueChanged += RaiseFlagsChanged;
        }

        public override void OnNetworkDespawn()
        {
            _controlFlags.OnValueChanged -= RaiseFlagsChanged;
        }

        private void RaiseFlagsChanged(ControlFlags previous, ControlFlags current)
        {
            OnControlFlagsChanged?.Invoke(previous, current);
        }

        // ---- Server API ----

        /// <summary>
        /// Server-only. Applies an effect, resolving the data's StackingPolicy (GS-5.2).
        /// </summary>
        public void ApplyEffect(EffectDataSO data, GameObject source = null)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[StatusEffectController] ApplyEffect is server-only.");
                return;
            }
            if (data == null) return;

            var existing = FindActive(data);
            if (existing != null)
            {
                switch (data.stackingPolicy)
                {
                    case StackingPolicy.Ignore:
                        return;

                    case StackingPolicy.Refresh:
                        existing.Remaining = data.duration;
                        break;

                    case StackingPolicy.StackDuration:
                        existing.Remaining += data.duration;
                        break;

                    case StackingPolicy.StackIntensity:
                        existing.Remaining = data.duration;
                        if (existing.Stacks < data.maxStacks)
                        {
                            existing.Stacks++;
                            existing.Runtime.OnStacksChanged(existing.Context, existing.Stacks);
                        }
                        break;
                }
                RebuildAggregatesAndSync();
                return;
            }

            var context = new StatusEffectContext(this, source);
            var effect = new ActiveEffect
            {
                Data = data,
                Runtime = data.CreateRuntime(),
                Context = context,
                Remaining = data.duration,
                TickTimer = data.tickInterval,
            };

            _active.Add(effect);
            effect.Runtime.OnApply(context);
            OnEffectApplied?.Invoke(data);
            RebuildAggregatesAndSync();
        }

        /// <summary>Server-only. Removes one effect by data reference (dispels, AllyRescue interactions).</summary>
        public bool RemoveEffect(EffectDataSO data)
        {
            if (!IsServer) return false;

            var effect = FindActive(data);
            if (effect == null) return false;

            ExpireEffect(effect);
            return true;
        }

        /// <summary>Server-only. Removes all debuffs (cleanse items / abilities).</summary>
        public void CleanseDebuffs()
        {
            if (!IsServer) return;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i].Data.isDebuff) ExpireEffect(_active[i]);
            }
        }

        /// <summary>Server-only. Clears everything (GS-2.4 run-reset / section transition).</summary>
        public void ClearAll()
        {
            if (!IsServer) return;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                ExpireEffect(_active[i]);
            }
        }

        public bool HasEffect(EffectDataSO data) => FindActive(data) != null;

        /// <summary>
        /// Bahadır kit support (GS-9): find the active runtime instance of a bespoke status
        /// class (e.g. SpywareMarkStatus) regardless of which EffectDataSO asset carries it.
        /// Backs EnemyRegistry.GetAllWithEffect&lt;T&gt;() — lets code query "is anyone marked"
        /// without holding a reference to the specific data asset.
        /// </summary>
        public T GetActiveEffectOfType<T>() where T : class, IStatusEffect
        {
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].Runtime is T match) return match;
            }
            return null;
        }

        // ---- Server tick loop ----

        private void Update()
        {
            if (!IsServer || _active.Count == 0) return;

            float dt = Time.deltaTime;
            bool dirty = false;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var effect = _active[i];

                if (effect.Data.tickInterval > 0f)
                {
                    effect.TickTimer -= dt;
                    while (effect.TickTimer <= 0f)
                    {
                        effect.Runtime.OnTick(effect.Context, effect.Data.tickInterval);
                        effect.TickTimer += effect.Data.tickInterval;
                    }
                }

                effect.Remaining -= dt;
                if (effect.Remaining <= 0f)
                {
                    ExpireEffect(effect, rebuild: false);
                    dirty = true;
                }
            }

            if (dirty) RebuildAggregatesAndSync();
        }

        // ---- Internals ----

        private ActiveEffect FindActive(EffectDataSO data)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].Data == data) return _active[i];
            }
            return null;
        }

        private void ExpireEffect(ActiveEffect effect, bool rebuild = true)
        {
            effect.Runtime.OnExpire(effect.Context);
            _active.Remove(effect);
            OnEffectExpired?.Invoke(effect.Data);
            if (rebuild) RebuildAggregatesAndSync();
        }

        private void RebuildAggregatesAndSync()
        {
            var flags = ControlFlags.None;
            float speed = 1f;

            foreach (var effect in _active)
            {
                flags |= effect.Data.controlFlags;
                speed *= effect.Data.moveSpeedMultiplier;
            }

            MoveSpeedMultiplier = speed;
            _controlFlags.Value = flags;

            _syncedEffects.Clear();
            float now = NetworkManager != null ? (float)NetworkManager.ServerTime.Time : Time.time;
            foreach (var effect in _active)
            {
                _syncedEffects.Add(new ActiveEffectSync
                {
                    EffectHash = effect.Data.EffectHash,
                    ExpiryServerTime = now + effect.Remaining,
                    Stacks = effect.Stacks,
                });
            }
        }
    }
}
