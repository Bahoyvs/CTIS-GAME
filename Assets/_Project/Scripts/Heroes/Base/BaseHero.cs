using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using CBuilding.Core;
using CBuilding.Data;

namespace CBuilding.Heroes
{
    /// <summary>
    /// Abstract base for all heroes — now a NetworkBehaviour.
    ///
    /// AUTHORITY MODEL:
    ///   - Health and speed multiplier are NetworkVariables: server-write, everyone-read.
    ///     Clients (including the owner) can only read; the ONLY way to change them is
    ///     server-side code (RPC handlers, AoE ticks, enemy AI). This is the "API validates,
    ///     client renders" contract from backend land.
    ///   - Movement stays owner-authoritative (see ClientNetworkTransform) for responsiveness.
    ///
    /// PREFAB SETUP: NetworkObject + ClientNetworkTransform + HeroStatController + concrete
    /// controller. Register the prefab in the NetworkManager's Network Prefabs list.
    /// </summary>
    [RequireComponent(typeof(HeroStatController))]
    public abstract class BaseHero : NetworkBehaviour, IDamageable
    {
        [Header("Base Hero")]
        [Tooltip("Layers this hero's attacks can hit (set to the Enemy layer).")]
        [SerializeField] protected LayerMask attackableLayers;

        // ------------------------------------------------------------ Replicated state
        // NetworkVariable = replicated column with an access policy. Delta-synced only when
        // the value actually changes — cheaper than any per-frame RPC for persistent state.
        private readonly NetworkVariable<float> _netHealth = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Multiplier so buffs stack cleanly on top of the locally-computed MoveSpeed stat.
        private readonly NetworkVariable<float> _netSpeedMultiplier = new(
            1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // ------------------------------------------------------------ Events (fire on ALL peers)
        public event Action<float, float> OnHealthChanged; // (current, max) — drives HP bars.
        public event Action<BaseHero> OnDied;
        public event Action<DamageInfo> OnDamaged;         // Server-only (DamageInfo isn't replicated).

        // GS-16: spawn registry so HUD panels (local HUD, teammate panel) can discover
        // heroes event-driven on every peer — no scene scans, no polling.
        public static readonly System.Collections.Generic.List<BaseHero> ActiveHeroes = new();
        public static event Action<BaseHero> OnHeroSpawned;
        public static event Action<BaseHero> OnHeroDespawned;

        public HeroStatController Stats { get; private set; }
        public float CurrentHealth => _netHealth.Value;
        public float SpeedMultiplier => _netSpeedMultiplier.Value;
        public bool IsAlive => _netHealth.Value > 0f;

        /// <summary>"Player_2 (Kerem)" — used everywhere by CombatLogManager.</summary>
        public string DisplayName =>
            $"Player_{OwnerClientId} ({(Stats != null && Stats.BaseStats != null ? Stats.BaseStats.HeroName : name)})";

        private Coroutine _speedBuffRoutine;
        private DamageModifierPipeline _damagePipeline; // Optional (GS-5.4); auto-added by StatusEffectController.

        protected virtual void Awake()
        {
            Stats = GetComponent<HeroStatController>();
            _damagePipeline = GetComponent<DamageModifierPipeline>();
        }

        public override void OnNetworkSpawn()
        {
            // OnValueChanged fires on every peer whenever the server writes — this replaces
            // the old direct event invocation and keeps remote HP bars in sync for free.
            _netHealth.OnValueChanged += HandleNetHealthChanged;

            if (IsServer)
            {
                _netHealth.Value = Stats.GetStat(StatType.MaxHealth);
                Stats.OnStatChanged += HandleStatChanged;
            }

            // Push initial value to freshly-spawned UI (late joiners get current, not max).
            OnHealthChanged?.Invoke(CurrentHealth, Stats.GetStat(StatType.MaxHealth));

            ActiveHeroes.Add(this);
            OnHeroSpawned?.Invoke(this);
        }

        public override void OnNetworkDespawn()
        {
            ActiveHeroes.Remove(this);
            OnHeroDespawned?.Invoke(this);

            _netHealth.OnValueChanged -= HandleNetHealthChanged;
            if (IsServer && Stats != null) Stats.OnStatChanged -= HandleStatChanged;
        }

        private void HandleNetHealthChanged(float previous, float current)
        {
            OnHealthChanged?.Invoke(current, Stats.GetStat(StatType.MaxHealth));
        }

        private void HandleStatChanged(StatType stat)
        {
            if (!IsServer || stat != StatType.MaxHealth || !IsAlive) return;
            _netHealth.Value = Mathf.Min(_netHealth.Value, Stats.GetStat(StatType.MaxHealth));
        }

        // ---------------------------------------------------------------- IDamageable (SERVER ONLY)

        public virtual void TakeDamage(in DamageInfo info)
        {
            // Hard authority gate: a client calling this locally is a no-op. Damage only
            // exists if the server says so.
            if (!IsServer || !IsAlive) return;

            // GS-5.4: ALL incoming damage runs through the modifier chain (SpywareMark,
            // Mark of Guilt, Sunburn...) before armor. Never special-case at call sites.
            float incoming = _damagePipeline != null ? _damagePipeline.Process(in info) : info.Amount;
            float mitigated = Mathf.Max(0f, incoming - Stats.GetStat(StatType.Armor));
            _netHealth.Value = Mathf.Max(0f, _netHealth.Value - mitigated);
            OnDamaged?.Invoke(info);

            CombatLogManager.LogAction(DisplayName, "took", $"{mitigated:F0} damage", transform.position);

            if (_netHealth.Value <= 0f) Die();
        }

        public virtual void Die()
        {
            if (!IsServer) return;
            CombatLogManager.LogAction(DisplayName, "was", "defeated", transform.position);
            DiedClientRpc();
        }

        /// <summary>Death presentation runs everywhere: local events, disable visuals, etc.</summary>
        [ClientRpc]
        private void DiedClientRpc()
        {
            OnDied?.Invoke(this);
            // MVP: leave the body; co-op down/revive flow (GDD roguelite death rules) hooks in here.
        }

        // ---------------------------------------------------------------- Server-side mutators
        // These are the ONLY entry points for other systems (AoE, items, enemy AI) to touch
        // replicated hero state. Public but self-guarding — safe to call from anywhere.

        public void ServerHeal(float amount)
        {
            if (!IsServer || !IsAlive || amount <= 0f) return;

            // GS-5.4: healing runs through the same modifier chain (anti-heal keys off
            // DamageFlags.Healing — e.g. Troll's stacking anti-heal).
            if (_damagePipeline != null)
            {
                var healInfo = new DamageInfo(amount, transform.position, Vector3.zero, 0f,
                    gameObject, DamageFlags.Healing);
                amount = _damagePipeline.Process(in healInfo);
                if (amount <= 0f) return;
            }

            _netHealth.Value = Mathf.Min(_netHealth.Value + amount, Stats.GetStat(StatType.MaxHealth));
        }

        public void ServerApplySpeedBuff(float multiplier, float duration)
        {
            if (!IsServer || !IsAlive) return;
            // Restart semantics: a fresh buff replaces the running one (no multiplicative stacking).
            if (_speedBuffRoutine != null) StopCoroutine(_speedBuffRoutine);
            _speedBuffRoutine = StartCoroutine(SpeedBuffRoutine(multiplier, duration));
        }

        private IEnumerator SpeedBuffRoutine(float multiplier, float duration)
        {
            _netSpeedMultiplier.Value = multiplier;
            yield return new WaitForSeconds(duration);
            _netSpeedMultiplier.Value = 1f;
            _speedBuffRoutine = null;
        }

        // ---------------------------------------------------------------- Hero Kit (GDD §3)

        /// <summary>Executed ON THE SERVER (called from the attack ServerRpc handler).</summary>
        public abstract void PerformBasicAttack(Vector3 aimPoint);

        /// <summary>Owner-local (movement is owner-authoritative).</summary>
        public virtual void PerformRoll(Vector3 direction) { }

        public virtual void PerformSkill1() { }
        public virtual void PerformSkill2() { }
        public virtual void PerformUltimate() { }
    }
}
