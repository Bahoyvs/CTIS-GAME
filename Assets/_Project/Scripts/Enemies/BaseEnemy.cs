using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using CBuilding.Core;
using CBuilding.Data;
using CBuilding.Heroes;
using CBuilding.StatusEffects;
using CBuilding.Utilities;

namespace CBuilding.Enemies
{
    /// <summary>
    /// Server-authoritative enemy (Module 4).
    ///
    /// AUTHORITY SPLIT:
    ///   SERVER : NavMeshAgent, state machine, target selection, damage math, knockback.
    ///   CLIENTS: receive position via NetworkTransform (server-auth, stock component),
    ///            facing via a 1-byte NetworkVariable, and hit/death presentation via
    ///            ClientRpcs. The agent is DISABLED on clients — they never simulate AI.
    ///
    /// PREFAB: BaseEnemy + NetworkObject + NetworkTransform (the regular one, NOT
    /// ClientNetworkTransform) + NavMeshAgent + collider on the Enemy layer.
    /// Register in Network Prefabs. Spawn server-side: Instantiate(...).Spawn(true).
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class BaseEnemy : NetworkBehaviour, IDamageable
    {
        protected enum EnemyState { Idle, Chase, Attack, Dead }

        [Header("Data")]
        [SerializeField] protected EnemyData data;

        [Header("Visuals")]
        [SerializeField] private SpriteRenderer bodyRenderer;
        [SerializeField] private Color flashColor = Color.white;
        [SerializeField] private float flashDuration = 0.12f;
        [SerializeField] private IsometricSprite8Dir spriteDirection;

        [Header("Feel")]
        [SerializeField] private float onHitShakeIntensity = 0.4f;
        [SerializeField] private float deathDespawnDelay = 2f;

        [Header("Spawn Entry")]
        [Tooltip("Seconds spent in the Spawning state after (re)spawn: untargetable, " +
                 "invulnerable and immobile while the entry presentation plays " +
                 "(EnemySpawnEntryPresenter). 0 = active instantly, no entry phase.")]
        [Min(0f)] [SerializeField] private float spawnEntryDuration = 1.2f;

        [Header("Targeting")]
        [Tooltip("How often the server re-evaluates who to chase (seconds).")]
        [SerializeField] private float retargetInterval = 0.5f;
        [Tooltip("1 point of threat outweighs this many meters of distance when scoring targets.")]
        [SerializeField] private float threatWeight = 0.15f;

        public event Action<BaseEnemy> OnDied;      // Server-side: wave managers subscribe.
        public event Action<DamageInfo> OnDamaged;  // Server-side: loot/score hooks.

        // Health lives in a NetworkVariable so late joiners and HP bars are always correct.
        private readonly NetworkVariable<float> _netHealth = new(
            1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Facing sector, mirroring the hero's approach: server computes, clients apply.
        private readonly NetworkVariable<byte> _netFacing = new(
            (byte)FacingDirection8.S,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        // Spawning state: replicated so clients drive the entry presentation locally with
        // ZERO extra RPC traffic — the initial value rides along in the spawn payload.
        private readonly NetworkVariable<bool> _netIsSpawning = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public bool IsAlive => _netHealth.Value > 0f;

        /// <summary>True while the entry phase runs (untargetable/invulnerable/immobile). All peers.</summary>
        public bool IsSpawning => _netIsSpawning.Value;

        /// <summary>Read-only surface for EnemySpawnEntryPresenter (subscribe, never poll).</summary>
        public NetworkVariable<bool> NetIsSpawning => _netIsSpawning;

        /// <summary>Entry phase length — presenter reads it so gameplay and visuals always agree.</summary>
        public float SpawnEntryDuration => spawnEntryDuration;
        public string DisplayName => $"{(data != null ? data.EnemyName : name)}#{NetworkObjectId}";

        // GS-16: read-only surface for the world-space HP billboard (EnemyWorldUI).
        // The UI subscribes to NetHealth.OnValueChanged — never polls.
        public NetworkVariable<float> NetHealth => _netHealth;
        public float MaxHealth => data != null ? data.MaxHealth : 1f;

        protected NavMeshAgent Agent { get; private set; }
        protected BaseHero CurrentTarget { get; private set; }
        protected EnemyState State { get; private set; } = EnemyState.Idle;

        // ---- Server-only working state ----
        private readonly Dictionary<ulong, float> _threatByClientId = new(); // Aggro table.
        private float _nextRetargetTime;
        private float _nextAttackTime;
        private float _stunEndTime;
        private float _spawnEntryEndTime;
        private Coroutine _flashRoutine;
        private GameObject _lastDamageInstigator; // GS-9: Bahadır Final Passive needs "who landed the kill".

        private MaterialPropertyBlock _mpb;
        private static readonly int ColorProp = Shader.PropertyToID("_Color");
        private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");

        // ---------------------------------------------------------------- Lifecycle

        private DamageModifierPipeline _damagePipeline; // Optional (GS-5.4).
        private StatusEffectController _statusEffects;  // Optional (GS-5.2); null-safe everywhere.

        protected virtual void Awake()
        {
            Agent = GetComponent<NavMeshAgent>();
            _mpb = new MaterialPropertyBlock();
            _damagePipeline = GetComponent<DamageModifierPipeline>();
            _statusEffects = GetComponent<StatusEffectController>();
        }

        public override void OnNetworkSpawn()
        {
            _netFacing.OnValueChanged += HandleFacingChanged;
            _netIsSpawning.OnValueChanged += HandleSpawningChanged;

            if (!IsServer)
            {
                // Clients are dumb terminals for enemies: kill the local simulation so the
                // agent can't fight the incoming NetworkTransform positions.
                Agent.enabled = false;

                // Pooled reuse: restore the pristine look, and mirror the untargetable
                // window — colliders stay off while the entry presentation plays.
                SetCollidersEnabled(!_netIsSpawning.Value);
                ResetPresentation();

                // Initial-sync facing (OnValueChanged doesn't fire for the spawn value).
                if (spriteDirection != null)
                    spriteDirection.SetDirection((FacingDirection8)_netFacing.Value);
                return;
            }

            if (data == null)
            {
                Debug.LogError($"[BaseEnemy] No EnemyData assigned on {name}.", this);
                enabled = false;
                return;
            }

            ResetServerState(); // Pool-safe: full runtime reset EVERY life, not just the first.

            _netHealth.Value = data.MaxHealth;
            Agent.speed = data.MoveSpeed;
            Agent.stoppingDistance = data.AttackRange * 0.9f;

            EnemySpawnHooks.RaiseSpawned(this); // GS-9: Bahadır Ultimate spawn-hack hook.
        }

        public override void OnNetworkDespawn()
        {
            _netFacing.OnValueChanged -= HandleFacingChanged;
            _netIsSpawning.OnValueChanged -= HandleSpawningChanged;
        }

        /// <summary>Clients mirror the untargetable window; the server toggles explicitly.</summary>
        private void HandleSpawningChanged(bool previous, bool current)
        {
            if (IsServer) return;
            SetCollidersEnabled(!current);
        }

        /// <summary>
        /// Server-side runtime reset for NetworkEnemyPool reuse. A pooled instance keeps its
        /// dead-state fields between lives (Dead state, disabled agent/colliders, threat
        /// table, timers) — everything must return to factory settings before the new life.
        /// Safe on the first life too (everything is already default).
        /// </summary>
        protected virtual void ResetServerState()
        {
            enabled = true;
            ResetPresentation();

            CurrentTarget = null;
            _threatByClientId.Clear();
            _lastDamageInstigator = null;
            _nextRetargetTime = 0f;
            _nextAttackTime = 0f;
            _stunEndTime = 0f;

            State = EnemyState.Idle; // Direct write: skip SetState's same-state guard vs Dead.

            if (spawnEntryDuration > 0f)
            {
                // Enter the Spawning state: untargetable (colliders off), invulnerable
                // (TakeDamage early-out), immobile (agent off). EndSpawnEntry() activates.
                _spawnEntryEndTime = Time.time + spawnEntryDuration;
                _netIsSpawning.Value = true;
                Agent.enabled = false;
                SetCollidersEnabled(false);
            }
            else
            {
                _netIsSpawning.Value = false;
                ActivateAgentAndColliders();
            }
        }

        /// <summary>Server-only. Spawning → active: the enemy becomes real to the world.</summary>
        private void EndSpawnEntry()
        {
            _netIsSpawning.Value = false;
            ActivateAgentAndColliders();
            _nextRetargetTime = 0f; // Wake up with a fresh target scan this frame.
        }

        private void ActivateAgentAndColliders()
        {
            SetCollidersEnabled(true);
            Agent.enabled = true;
            if (!Agent.isOnNavMesh) Agent.Warp(transform.position);
            if (Agent.isOnNavMesh)
            {
                Agent.isStopped = true;
                Agent.velocity = Vector3.zero;
            }
        }

        /// <summary>Clears any leftover hit-flash tint (runs on server and clients).</summary>
        private void ResetPresentation()
        {
            if (_flashRoutine != null)
            {
                StopCoroutine(_flashRoutine);
                _flashRoutine = null;
            }
            if (bodyRenderer != null) bodyRenderer.SetPropertyBlock(null);
        }

        private void HandleFacingChanged(byte previous, byte current)
        {
            if (IsServer || spriteDirection == null) return; // Server applied it locally already.
            spriteDirection.SetDirection((FacingDirection8)current);
        }

        protected virtual void Update()
        {
            // THE Module-4 rule: the brain runs nowhere but the server.
            if (!IsServer || State == EnemyState.Dead) return;

            // Spawning state: no brain, no aggro, no movement until the entry finishes.
            if (_netIsSpawning.Value)
            {
                if (Time.time >= _spawnEntryEndTime) EndSpawnEntry();
                return;
            }

            if (Time.time < _stunEndTime) return; // Hitstun: brain off, knockback still displaces.

            // GS-5 hard-CC (previously a README TODO): a Stun/Freeze status pauses the brain
            // exactly like hitstun does. First real consumer: Gobluna S2's purge stun —
            // Bahadır's Fx_Stun gains actual enemy lockdown from this too.
            if (_statusEffects != null && _statusEffects.IsStunned)
            {
                if (Agent.enabled && Agent.isOnNavMesh)
                {
                    Agent.isStopped = true;
                    Agent.velocity = Vector3.zero;
                }
                return;
            }

            if (Time.time >= _nextRetargetTime)
            {
                _nextRetargetTime = Time.time + retargetInterval;
                CurrentTarget = SelectTarget();
            }

            if (CurrentTarget == null)
            {
                SetState(EnemyState.Idle);
                TickIdle();
                return;
            }

            float distToTarget = Vector3.Distance(transform.position, CurrentTarget.transform.position);

            switch (State)
            {
                case EnemyState.Idle:
                    if (distToTarget <= data.AggroRange) SetState(EnemyState.Chase);
                    break;
                case EnemyState.Chase:
                    if (distToTarget <= data.AttackRange) SetState(EnemyState.Attack);
                    break;
                case EnemyState.Attack:
                    if (distToTarget > data.AttackRange * 1.15f) SetState(EnemyState.Chase); // Hysteresis.
                    break;
            }

            switch (State)
            {
                case EnemyState.Idle:   TickIdle(); break;
                case EnemyState.Chase:  TickChase(); break;
                case EnemyState.Attack: TickAttack(); break;
            }

            UpdateFacing();
        }

        // ---------------------------------------------------------------- Targeting (SERVER)

        /// <summary>
        /// Dynamic target selection over all connected players' NetworkObjects.
        /// Score = distance - threat * weight: closest hero wins by default, but a hero who
        /// has dealt damage (threat) pulls aggro even from slightly further away — the
        /// classic MMO threat-table model, minimal edition.
        /// </summary>
        protected virtual BaseHero SelectTarget()
        {
            BaseHero best = null;
            float bestScore = float.MaxValue;

            foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null) continue;
                if (!client.PlayerObject.TryGetComponent(out BaseHero hero) || !hero.IsAlive) continue;

                float dist = Vector3.Distance(transform.position, hero.transform.position);

                // Untargeted heroes beyond aggro range stay invisible to us; heroes with
                // threat (they hit us!) are valid targets at any distance.
                _threatByClientId.TryGetValue(client.ClientId, out float threat);
                if (threat <= 0f && dist > data.AggroRange) continue;

                // GS-9 (Bahadır Feature): 100% ghost — stealth ALWAYS hides from targeting,
                // even if the hero already had threat/aggro on this enemy. (Previously an
                // already-engaged enemy kept chasing through Stealth; that contradicts
                // "enemies can't see him." BahadirStealthGuard also force-drops any
                // CurrentTarget lock the instant Stealth turns on, so this isn't just a
                // "no NEW target" rule — existing locks break immediately too.)
                if (hero.TryGetComponent<StatusEffectController>(out var heroStatus) && heroStatus.IsStealthed)
                    continue;

                float score = dist - threat * threatWeight;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = hero;
                }
            }
            return best;
        }

        /// <summary>
        /// Server-only. Immediately clears this enemy's target lock if it's currently
        /// pointed at <paramref name="hero"/> — a no-op otherwise. Used by
        /// BahadirStealthGuard so "enemies can't see him" takes effect the instant Stealth
        /// turns on, instead of waiting up to retargetInterval for the next natural
        /// SelectTarget() re-evaluation (which will also decline to reacquire him — see
        /// the Stealth check in SelectTarget).
        /// </summary>
        public void ForceDropTarget(BaseHero hero)
        {
            if (!IsServer || CurrentTarget != hero) return;
            CurrentTarget = null;
            SetState(EnemyState.Idle);
        }

        private void AddThreat(GameObject instigator, float amount)
        {
            if (instigator == null || !instigator.TryGetComponent(out NetworkObject netObj)) return;
            ulong clientId = netObj.OwnerClientId;
            _threatByClientId.TryGetValue(clientId, out float current);
            _threatByClientId[clientId] = current + amount;
        }

        // ---------------------------------------------------------------- States (SERVER)

        protected void SetState(EnemyState next)
        {
            if (State == next) return;
            State = next;
            OnEnterState(next);
        }

        protected virtual void OnEnterState(EnemyState next)
        {
            if (!Agent.enabled || !Agent.isOnNavMesh) return;
            Agent.isStopped = next != EnemyState.Chase;
        }

        protected virtual void TickIdle() { /* Patrol override hook. */ }

        protected virtual void TickChase()
        {
            if (!Agent.enabled || !Agent.isOnNavMesh) return;
            Agent.isStopped = false; // Re-arm after hitstun paused the agent mid-chase.
            Agent.SetDestination(CurrentTarget.transform.position);
        }

        protected virtual void TickAttack()
        {
            if (Time.time < _nextAttackTime) return;
            _nextAttackTime = Time.time + data.AttackCooldown;

            // Server-to-server call: hero.TakeDamage guards IsServer and writes the hero's
            // health NetworkVariable, which replicates to everyone. No RPC needed here.
            Vector3 knockDir = CurrentTarget.transform.position - transform.position;
            CurrentTarget.TakeDamage(new DamageInfo(
                data.AttackDamage, CurrentTarget.transform.position, knockDir, 2f, gameObject,
                DamageFlags.Melee));

            CombatLogManager.LogAction(DisplayName, "used", $"Melee_Attack on {CurrentTarget.DisplayName}",
                transform.position);
        }

        private void UpdateFacing()
        {
            if (spriteDirection == null || CurrentTarget == null) return;

            Vector3 facing = Agent.velocity.sqrMagnitude > 0.1f
                ? Agent.velocity
                : CurrentTarget.transform.position - transform.position;

            // Host-server computes the sector locally (camera orientation is identical on
            // every machine — fixed 45° iso rig), then replicates the 1-byte result.
            spriteDirection.SetFacing(facing);
            byte sector = (byte)spriteDirection.CurrentDirection;
            if (_netFacing.Value != sector) _netFacing.Value = sector;
        }

        // ---------------------------------------------------------------- IDamageable (SERVER)

        /// <summary>
        /// Server-only direct health write for scripted mechanics (Phoenix-Ghoul rebirth
        /// egg and similar death-interceptors). Clamped to [0, MaxHealth]. Deliberately
        /// bypasses the damage pipeline — this is a scripted state change, not a hit.
        /// </summary>
        protected void ServerSetHealth(float value)
        {
            if (!IsServer) return;
            _netHealth.Value = Mathf.Clamp(value, 0f, MaxHealth);
        }

        public virtual void TakeDamage(in DamageInfo info)
        {
            // Damage originates from HeroController.PerformBasicAttack, which only runs
            // server-side (reached via AttackServerRpc). This guard makes the contract explicit.
            if (!IsServer || !IsAlive) return;

            // Spawning state = invulnerable. Colliders are off so hits shouldn't even land,
            // but collider-less damage paths (DoTs applied at spawn, zones) end here too.
            if (_netIsSpawning.Value) return;

            // GS-5.4: run the modifier chain (marks, vulnerability effects) before health math.
            float amount = _damagePipeline != null ? _damagePipeline.Process(in info) : info.Amount;

            _netHealth.Value = Mathf.Max(0f, _netHealth.Value - amount);
            OnDamaged?.Invoke(info);
            AddThreat(info.Instigator, amount); // Damage = aggro.
            if (info.Instigator != null) _lastDamageInstigator = info.Instigator; // GS-9: last-hit tracking.

            // GS-9 (Gobluna Siphoner + Green Fire passives): hero-sourced damage is a team
            // event, mirroring RaiseAllyKilledEnemy in Die(). Post-pipeline amount — passives
            // that scale with "damage dealt" should see what actually landed.
            if (amount > 0f && info.Instigator != null && info.Instigator.GetComponent<BaseHero>() != null)
            {
                TeamEventBus.RaiseAllyDealtDamage(info.Instigator, gameObject, amount);
            }

            // DoT ticks (GS-5) deal damage only — no hitstun/knockback/hit-flash, otherwise
            // a poison would stun-lock the AI brain every tick.
            bool isDoT = (info.Flags & DamageFlags.DoT) != 0;

            if (!isDoT)
            {
                // Server-side reaction: interrupt the brain + physical displacement.
                _stunEndTime = Time.time + data.HitStunDuration;
                if (Agent.enabled && Agent.isOnNavMesh)
                {
                    Agent.isStopped = true;
                    Agent.velocity = Vector3.zero;
                }
                if (GameFeelManager.Instance != null)
                {
                    GameFeelManager.Instance.ApplyKnockback(
                        Agent, info.KnockbackDirection,
                        info.KnockbackForce * data.KnockbackResistanceMultiplier);
                }
            }

            CombatLogManager.LogAction(DisplayName, "took", $"{amount:F0} damage", transform.position);

            // One broadcast so every player SEES the impact at the same moment.
            if (!isDoT) HitReactionClientRpc(info.HitPoint);

            if (_netHealth.Value <= 0f) Die();
        }

        /// <summary>
        /// [ClientRpc]: presentation-only hit feedback, mirrored on all clients (host included).
        /// Note: the host's hitstop scales Time.timeScale on the machine that IS the server,
        /// so the simulation itself hiccups ~50ms for everyone. At light-hitstop durations
        /// this is imperceptible and NetworkTransform interpolation absorbs it; if it ever
        /// bothers you, swap hitstop for a sprite-freeze on remote machines.
        /// </summary>
        [ClientRpc]
        private void HitReactionClientRpc(Vector3 hitPoint)
        {
            if (_flashRoutine != null) StopCoroutine(_flashRoutine);
            _flashRoutine = StartCoroutine(FlashRoutine());

            if (GameFeelManager.Instance != null)
            {
                GameFeelManager.Instance.DoLightHitstop();
                GameFeelManager.Instance.RequestScreenShake(onHitShakeIntensity);
            }
            // hitPoint: spawn hit VFX / damage numbers here.
        }

        public virtual void Die()
        {
            if (!IsServer || State == EnemyState.Dead) return;

            SetState(EnemyState.Dead);
            OnDied?.Invoke(this);
            CombatLogManager.LogAction(DisplayName, "was", "slain", transform.position);

            // GS-9 (Bahadır Final Passive): "an ally landed the kill" — only fires for
            // hero-sourced kills, never DoT/self/enemy-on-enemy.
            if (_lastDamageInstigator != null && _lastDamageInstigator.GetComponent<BaseHero>() != null)
            {
                TeamEventBus.RaiseAllyKilledEnemy(_lastDamageInstigator, this);
            }

            Agent.enabled = false;
            SetCollidersEnabled(false); // Server-side: corpse stops blocking/receiving hits NOW.
            DiedClientRpc();

            StartCoroutine(DespawnAfterDelay());
        }

        [ClientRpc]
        private void DiedClientRpc()
        {
            SetCollidersEnabled(false); // Client-side mirrors (host already did it — idempotent).
            // Hook: death animation / dissolve. Object vanishes when the server despawns it.
        }

        private IEnumerator DespawnAfterDelay()
        {
            yield return new WaitForSeconds(deathDespawnDelay);

            // Pool hygiene: expire leftover status effects (marks, DoTs) NOW, so a recycled
            // instance never wakes up in its next life still carrying old debuffs.
            if (TryGetComponent<StatusEffectController>(out var status)) status.ClearAll();
            // Despawn(true) destroys on the server and replicates destruction to all clients —
            // the networked replacement for Destroy(gameObject, delay).
            // POOLING: when NetworkEnemyPool has a handler for this prefab, NGO routes this
            // "destroy" into the pool on every peer instead — the object is deactivated and
            // recycled, never actually destroyed. No change needed here.
            if (NetworkObject.IsSpawned) NetworkObject.Despawn(true);
        }

        private void SetCollidersEnabled(bool value)
        {
            foreach (Collider col in GetComponentsInChildren<Collider>())
                col.enabled = value;
        }

        // ---------------------------------------------------------------- Hit Flash (ALL PEERS)

        private IEnumerator FlashRoutine()
        {
            if (bodyRenderer == null) yield break;

            float elapsed = 0f;
            while (elapsed < flashDuration)
            {
                elapsed += Time.deltaTime;
                Color tint = Color.Lerp(flashColor, Color.white, elapsed / flashDuration);

                // MaterialPropertyBlock: per-renderer tint without instantiating the material.
                bodyRenderer.GetPropertyBlock(_mpb);
                _mpb.SetColor(ColorProp, tint);
                _mpb.SetColor(BaseColorProp, tint);
                bodyRenderer.SetPropertyBlock(_mpb);
                yield return null;
            }

            bodyRenderer.SetPropertyBlock(null);
            _flashRoutine = null;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (data == null) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, data.AggroRange);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, data.AttackRange);
        }
#endif
    }
}
