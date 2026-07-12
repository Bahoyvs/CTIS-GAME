using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using CBuilding.Core;
using CBuilding.Data;
using CBuilding.Heroes;
using CBuilding.StatusEffects;

namespace CBuilding.Enemies
{
    /// <summary>
    /// Universal roster enemy (Tiers 1–3 of the Unified Enemy Roster). Extends BaseEnemy with:
    ///   - attack delegation to an optional <see cref="EnemyAttackBehaviour"/> (ranged volley,
    ///     cone breath/sweep) — no component means plain melee, exactly like BaseEnemy;
    ///   - a keyed move-/attack-speed multiplier registry (Screamer shriek, Blood-Hound frenzy,
    ///     Big Bertha enrage) with optional expiry, plus StatusEffectController.MoveSpeedMultiplier
    ///     integration so hero slows finally affect enemy agents;
    ///   - slow-immunity windows (Screamer: buffed zombies ignore slows for the buff duration);
    ///   - server events mechanic components hook into: OnTargetSwitched, OnMeleeHitLanded;
    ///   - death interception (<see cref="IDeathInterceptor"/> — Phoenix-Ghoul rebirth egg);
    ///   - a BrainSuspended state (egg phase: alive and damageable but no AI/movement/attacks).
    ///
    /// All additions are server-side; clients remain the dumb terminals BaseEnemy made them.
    /// Pool-safe: every registry/flag resets in ResetServerState() each life.
    /// </summary>
    public class RosterEnemy : BaseEnemy
    {
        private class TimedMod
        {
            public object Key;
            public float Mult;
            public float Expiry; // <= 0 = until removed explicitly
        }

        /// <summary>Server-side: (oldTarget, newTarget). Fired from the brain, never on clients.</summary>
        public event Action<BaseHero, BaseHero> OnTargetSwitched;

        /// <summary>Server-side: default melee connected with a hero (Leaper slow, Blood-Hound calm-down).</summary>
        public event Action<BaseHero> OnMeleeHitLanded;

        public EnemyData Data => data;
        public BaseHero Target => CurrentTarget;
        public NavMeshAgent NavAgent => Agent;

        /// <summary>Egg phase & co.: alive and damageable, but no AI, movement or attacks.</summary>
        public bool BrainSuspended { get; private set; }

        private readonly List<TimedMod> _speedMods = new();
        private readonly List<TimedMod> _attackSpeedMods = new();
        private float _slowImmunityUntil;
        private BaseHero _lastTarget;
        private float _nextAttackTimeLocal;
        private EnemyAttackBehaviour _attackBehaviour;
        private StatusEffectController _status;
        private IDeathInterceptor[] _deathInterceptors;

        protected override void Awake()
        {
            base.Awake();
            _attackBehaviour = GetComponent<EnemyAttackBehaviour>();
            _status = GetComponent<StatusEffectController>();
            _deathInterceptors = GetComponents<IDeathInterceptor>();
        }

        protected override void ResetServerState()
        {
            base.ResetServerState();
            _speedMods.Clear();
            _attackSpeedMods.Clear();
            _slowImmunityUntil = 0f;
            _lastTarget = null;
            _nextAttackTimeLocal = 0f;
            BrainSuspended = false;
        }

        protected override void Update()
        {
            // Suspended (egg phase): keep the body pinned, skip the whole brain. Damage
            // still flows through TakeDamage — the egg can be burst.
            if (IsServer && BrainSuspended && State != EnemyState.Dead)
            {
                if (Agent.enabled && Agent.isOnNavMesh)
                {
                    Agent.isStopped = true;
                    Agent.velocity = Vector3.zero;
                }
                return;
            }

            base.Update();

            if (!IsServer || State == EnemyState.Dead || IsSpawning) return;

            // Target-switch detection by polling (base assigns CurrentTarget privately).
            if (!ReferenceEquals(CurrentTarget, _lastTarget))
            {
                BaseHero old = _lastTarget;
                _lastTarget = CurrentTarget;
                OnTargetSwitched?.Invoke(old, CurrentTarget);
            }

            ApplyMoveSpeed();
        }

        // ---------------------------------------------------------------- Speed registries (SERVER)

        /// <summary>Stacking, keyed move-speed multiplier. duration &lt;= 0 = until removed.</summary>
        public void AddSpeedMultiplier(object key, float multiplier, float duration = -1f)
            => AddMod(_speedMods, key, multiplier, duration);

        public void RemoveSpeedMultiplier(object key) => RemoveMod(_speedMods, key);

        /// <summary>Attack-speed multiplier: 2 = attacks twice as fast (cooldown halved).</summary>
        public void AddAttackSpeedMultiplier(object key, float multiplier, float duration = -1f)
            => AddMod(_attackSpeedMods, key, multiplier, duration);

        public void RemoveAttackSpeedMultiplier(object key) => RemoveMod(_attackSpeedMods, key);

        /// <summary>While active, status-effect slows (multiplier &lt; 1) are ignored. Screamer shriek.</summary>
        public void GrantSlowImmunity(float duration)
            => _slowImmunityUntil = Mathf.Max(_slowImmunityUntil, Time.time + duration);

        public float CurrentAttackCooldown
            => data.AttackCooldown / Mathf.Max(0.01f, Product(_attackSpeedMods));

        private static void AddMod(List<TimedMod> list, object key, float mult, float duration)
        {
            foreach (TimedMod m in list)
            {
                if (m.Key != key) continue;
                m.Mult = mult;
                m.Expiry = duration > 0f ? Time.time + duration : -1f;
                return;
            }
            list.Add(new TimedMod
            {
                Key = key,
                Mult = mult,
                Expiry = duration > 0f ? Time.time + duration : -1f
            });
        }

        private static void RemoveMod(List<TimedMod> list, object key)
        {
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i].Key == key) list.RemoveAt(i);
        }

        /// <summary>Product of all live multipliers; prunes expired entries in the same pass.</summary>
        private static float Product(List<TimedMod> list)
        {
            float product = 1f;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                TimedMod m = list[i];
                if (m.Expiry > 0f && Time.time >= m.Expiry) { list.RemoveAt(i); continue; }
                product *= m.Mult;
            }
            return product;
        }

        private void ApplyMoveSpeed()
        {
            if (!Agent.enabled) return;

            float statusMult = _status != null ? _status.MoveSpeedMultiplier : 1f;
            if (statusMult < 1f && Time.time < _slowImmunityUntil) statusMult = 1f; // Shriek immunity.

            Agent.speed = data.MoveSpeed * Product(_speedMods) * statusMult;
        }

        // ---------------------------------------------------------------- Attack (SERVER)

        protected override void TickAttack()
        {
            if (CurrentTarget == null || Time.time < _nextAttackTimeLocal) return;
            _nextAttackTimeLocal = Time.time + CurrentAttackCooldown;

            if (_attackBehaviour != null && _attackBehaviour.enabled)
            {
                _attackBehaviour.ExecuteAttack(this, CurrentTarget);
                return;
            }
            PerformDefaultMelee(CurrentTarget);
        }

        /// <summary>
        /// The BaseEnemy melee, re-exposed so attack behaviours can fall back to it between
        /// specials (Wyrmling bites between breaths). Raises OnMeleeHitLanded.
        /// </summary>
        public void PerformDefaultMelee(BaseHero target)
        {
            if (target == null || !target.IsAlive) return;

            Vector3 knockDir = target.transform.position - transform.position;
            target.TakeDamage(new DamageInfo(
                data.AttackDamage, target.transform.position, knockDir, 2f, gameObject,
                DamageFlags.Melee));

            CombatLogManager.LogAction(DisplayName, "used", $"Melee_Attack on {target.DisplayName}",
                transform.position);
            OnMeleeHitLanded?.Invoke(target);
        }

        // ---------------------------------------------------------------- Death interception (SERVER)

        public override void Die()
        {
            if (!IsServer || State == EnemyState.Dead) return;

            if (_deathInterceptors != null)
            {
                foreach (IDeathInterceptor interceptor in _deathInterceptors)
                    if (interceptor.TryInterceptDeath(this)) return; // Cheated death (this time).
            }
            base.Die();
        }

        /// <summary>Server-only scripted heal/revive used by death interceptors.</summary>
        public void ServerRestoreHealth(float value) => ServerSetHealth(value);

        /// <summary>Server-only. True = alive but inert (egg phase). See BrainSuspended.</summary>
        public void SetBrainSuspended(bool suspended)
        {
            if (!IsServer) return;
            BrainSuspended = suspended;
        }
    }
}
