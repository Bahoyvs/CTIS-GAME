using System.Collections.Generic;
using CBuilding.Abilities;
using CBuilding.Abilities.Delivery;
using CBuilding.Core;
using CBuilding.Enemies;
using CBuilding.StatusEffects;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Heroes.Gobluna
{
    /// <summary>
    /// GS-9 — server-authoritative state machine for Gobluna's Skill2 ("Green Fire
    /// Purge &amp; Stun"). Sits next to AbilityController on the Gobluna prefab; the thin
    /// GoblunaSkill2Runtime bridges the slot's CanActivate/Execute into this component,
    /// which owns everything the slot system can't express:
    ///
    ///   THE LOCK — casting the cone marks enemies with the permanent Fx_GreenFire DoT
    ///   and LOCKS the skill. It unlocks when every burning enemy is dead (tracked via
    ///   BaseEnemy.OnDied + a slow prune for pool-clears), or becomes castable early
    ///   when the resource bar is full.
    ///
    ///   THE RESOURCE — a replicated 0..resourceMax bar (NetworkVariable, everyone-read,
    ///   for the HUD). Fills mostly from Gobluna's OUTGOING heals
    ///   (TeamEventBus.OnAllyHealedAlly, post-clamp amounts — overheal is worthless)
    ///   plus a slow passive trickle while locked.
    ///
    ///   THE PURGE — recasting with a full bar consumes it, STUNS every enemy currently
    ///   burning with HER green fire, strips the fire (it is *purged* — fuel for the
    ///   stun), and unlocks the skill for a fresh cone.
    ///
    /// Burning-target discovery costs nothing extra: ApplyStatusEffectSO.OnAnyStatusApplied
    /// (the generic hook Bahadır's Skill2 already uses) tells us every time OUR Fx_GreenFire
    /// lands, regardless of which delivery carried it.
    /// </summary>
    [RequireComponent(typeof(AbilityController))]
    public class GoblunaSkill2Controller : NetworkBehaviour
    {
        [Header("Green Fire (must match the ApplyStatus effect inside CA_Gobluna_S2_Cone)")]
        [Tooltip("Fx_GreenFire — the permanent DoT. The lock and the purge-stun both key off THIS asset.")]
        [SerializeField] private EffectDataSO greenFireDoT;

        [Header("Purge")]
        [Tooltip("Fx_GoblunaStun — ControlFlags.Stun, ~1.5-2s, Refresh. Applied to every burning enemy on reactivation.")]
        [SerializeField] private EffectDataSO purgeStunEffect;
        [Tooltip("Purge strips Fx_GreenFire from the stunned targets (the fire is CONSUMED by the stun). Off = they keep burning and the skill stays locked until they die.")]
        [SerializeField] private bool purgeRemovesGreenFire = true;

        [Header("Resource bar")]
        [Min(1f)] [SerializeField] private float resourceMax = 100f;
        [Tooltip("Resource per point of HP she actually restores to allies ('fills mostly when Gobluna heals').")]
        [Min(0f)] [SerializeField] private float resourcePerHealPoint = 0.5f;
        [Tooltip("Passive trickle per second while the skill is locked ('and slightly over time').")]
        [Min(0f)] [SerializeField] private float passiveFillPerSecond = 1f;

        [Tooltip("Seconds between slow validity sweeps of the burning set (catches pool-clears and section transitions that remove effects without a death event).")]
        [Min(0.1f)] [SerializeField] private float pruneInterval = 0.5f;

        // ---- Replicated state (server-write, everyone-read → HUD binds directly) ----

        private readonly NetworkVariable<float> _netResource = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _netLocked = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Any-peer: current resource (0..ResourceMax). HUD subscribes to OnValueChanged.</summary>
        public NetworkVariable<float> NetResource => _netResource;

        /// <summary>Any-peer: is Skill2 currently locked behind burning enemies? (HUD grey-out / bar glow.)</summary>
        public NetworkVariable<bool> NetLocked => _netLocked;

        /// <summary>Bar denominator for the HUD (same value on every peer — serialized field).</summary>
        public float ResourceMax => resourceMax;

        // ---- Server-only working state ----

        private readonly HashSet<BaseEnemy> _burning = new();
        private readonly List<BaseEnemy> _scratch = new(); // prune/purge iteration without set mutation
        private float _nextPruneTime;

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            ApplyStatusEffectSO.OnAnyStatusApplied += HandleAnyStatusApplied;
            TeamEventBus.OnAllyHealedAlly += HandleAllyHealedAlly;
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer) return;

            ApplyStatusEffectSO.OnAnyStatusApplied -= HandleAnyStatusApplied;
            TeamEventBus.OnAllyHealedAlly -= HandleAllyHealedAlly;

            foreach (BaseEnemy enemy in _burning)
            {
                if (enemy != null) enemy.OnDied -= HandleBurningEnemyDied;
            }
            _burning.Clear();
        }

        // ---------------------------------------------------------------- Slot bridge (SERVER)

        /// <summary>Extra gating for GoblunaSkill2Runtime.CanActivate (base cooldown is 0).</summary>
        public bool CanCast => !_netLocked.Value || _netResource.Value >= resourceMax;

        /// <summary>
        /// The actual cast, from GoblunaSkill2Runtime.Execute. Two faces of one button:
        /// unlocked = fire the cone (locking happens reactively when marks land);
        /// locked + full bar = Step 2, the purge-stun.
        /// </summary>
        public void ServerCast(ComposedAbilitySO coneAbility, AbilityController abilities, Vector3 aimPoint)
        {
            if (!IsServer) return;

            if (_netLocked.Value && _netResource.Value >= resourceMax)
            {
                ExecutePurge();
                return;
            }

            // Fresh cone. If it marks nobody (whiffed into empty space), no mark event
            // arrives and the skill simply stays unlocked — free re-aim, by design.
            coneAbility?.ExecuteDelivery(abilities, aimPoint);
        }

        // ---------------------------------------------------------------- Lock tracking (SERVER)

        private void HandleAnyStatusApplied(EffectDataSO effect, GameObject caster, GameObject target)
        {
            if (effect == null || effect != greenFireDoT) return;
            if (caster != gameObject) return; // HER green fire only — a second Gobluna tracks her own
            if (!target.TryGetComponent<BaseEnemy>(out var enemy) || !enemy.IsAlive) return;

            if (_burning.Add(enemy))
            {
                enemy.OnDied += HandleBurningEnemyDied;
            }
            _netLocked.Value = true;
        }

        private void HandleBurningEnemyDied(BaseEnemy enemy)
        {
            enemy.OnDied -= HandleBurningEnemyDied;
            _burning.Remove(enemy);
            if (_burning.Count == 0) Unlock();
        }

        /// <summary>
        /// "All burning enemies died" → the lock opens. Accrued resource is KEPT (only the
        /// purge consumes it) — banking a near-full bar into the next cone is the reward
        /// for finishing the previous pack.
        /// </summary>
        private void Unlock()
        {
            _netLocked.Value = false;
        }

        private void Update()
        {
            if (!IsServer || !_netLocked.Value) return;

            // Passive trickle only while locked — an unlocked bar shouldn't quietly
            // pre-charge the NEXT lock's escape hatch for free.
            if (passiveFillPerSecond > 0f)
            {
                AddResource(passiveFillPerSecond * Time.deltaTime);
            }

            // Slow sweep: deaths are event-driven (OnDied above); this catches the quiet
            // exits — pooled despawn-without-death, section ClearAll stripping the DoT.
            if (Time.time >= _nextPruneTime)
            {
                _nextPruneTime = Time.time + pruneInterval;
                PruneBurningSet();
            }
        }

        private void PruneBurningSet()
        {
            _scratch.Clear();
            foreach (BaseEnemy enemy in _burning)
            {
                bool stillBurning = enemy != null && enemy.IsAlive && enemy.isActiveAndEnabled &&
                                    enemy.TryGetComponent<StatusEffectController>(out var status) &&
                                    status.HasEffect(greenFireDoT);
                if (!stillBurning) _scratch.Add(enemy);
            }

            for (int i = 0; i < _scratch.Count; i++)
            {
                BaseEnemy stale = _scratch[i];
                if (stale != null) stale.OnDied -= HandleBurningEnemyDied;
                _burning.Remove(stale);
            }

            if (_burning.Count == 0) Unlock();
        }

        // ---------------------------------------------------------------- Resource (SERVER)

        private void HandleAllyHealedAlly(GameObject healer, GameObject target, float amount)
        {
            if (healer != gameObject) return; // only HER outgoing heals charge the bar
            AddResource(amount * resourcePerHealPoint);
        }

        private void AddResource(float amount)
        {
            if (amount <= 0f) return;
            float next = Mathf.Clamp(_netResource.Value + amount, 0f, resourceMax);
            if (!Mathf.Approximately(next, _netResource.Value))
            {
                _netResource.Value = next;
            }
        }

        // ---------------------------------------------------------------- Step 2: the purge (SERVER)

        private void ExecutePurge()
        {
            _netResource.Value = 0f; // the bar IS the cast cost

            _scratch.Clear();
            _scratch.AddRange(_burning); // ApplyEffect/RemoveEffect below must not mutate mid-iteration

            for (int i = 0; i < _scratch.Count; i++)
            {
                BaseEnemy enemy = _scratch[i];
                if (enemy == null || !enemy.IsAlive) continue;
                if (!enemy.TryGetComponent<StatusEffectController>(out var status)) continue;

                if (purgeStunEffect != null)
                {
                    // Direct ApplyEffect (Bahadır chain-mark precedent): the target set is
                    // already known, so no delivery/targeting pass is needed — and skipping
                    // ApplyStatusEffectSO keeps OnAnyStatusApplied listeners out of it.
                    status.ApplyEffect(purgeStunEffect, gameObject);
                }

                if (purgeRemovesGreenFire && greenFireDoT != null)
                {
                    status.RemoveEffect(greenFireDoT);
                }
            }

            CombatLogManager.LogAction(name, "used", "GreenFire_Purge_Stun", transform.position);

            if (purgeRemovesGreenFire)
            {
                // Fire consumed everywhere → the lock opens now, not at the next prune.
                foreach (BaseEnemy enemy in _burning)
                {
                    if (enemy != null) enemy.OnDied -= HandleBurningEnemyDied;
                }
                _burning.Clear();
                Unlock();
            }
            // else: they still burn; the lock stays until they die (next prune/death event).
        }
    }
}
