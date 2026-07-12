using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem; // Project is set to "Input System Package (New)" only.
using CBuilding.Abilities;
using CBuilding.Core;
using CBuilding.Data;
using CBuilding.StatusEffects;
using CBuilding.Utilities;

namespace CBuilding.Heroes
{
    /// <summary>
    /// Networked player controller.
    ///
    /// SPLIT OF RESPONSIBILITIES (the core NGO discipline):
    ///   OWNER (IsOwner)  : reads input, moves locally (ClientNetworkTransform replicates),
    ///                      computes aim + 8-dir facing, REQUESTS combat via ServerRpc.
    ///   SERVER (IsServer): validates and executes combat (cooldowns, hit detection, damage).
    ///   ALL CLIENTS      : receive presentation via ClientRpcs / NetworkVariable callbacks.
    ///
    /// TRAFFIC BUDGET: movement rides on NetworkTransform deltas; facing is a 1-byte
    /// NetworkVariable that only syncs when the sector changes; attacks are one small
    /// ServerRpc per swing. Nothing here sends per-frame RPCs.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class HeroController : BaseHero
    {
        private const float Gravity = -25f;

        [Header("Scene References")]
        [SerializeField] private Camera isoCamera;
        [SerializeField] private IsometricSprite8Dir spriteDirection;

        [Header("Basic Attack (GS-17)")]
        [Tooltip("The hero's WeaponVisualController (child WeaponVisual). Recoil is triggered from AttackSwingClientRpc so every client plays it in sync with the confirmed attack.")]
        [SerializeField] private WeaponVisualController weaponVisual;
        [Tooltip("Press shorter than this = tap attack; longer = hold branch (only for IHoldableBasicAttack heroes). GS-17 §6.3: tap does A, hold does B — NOT hold-to-charge.")]
        [SerializeField] private float holdThreshold = 0.18f;

        [Header("Roll (GDD: Shift dash)")]
        [SerializeField] private float rollDuration = 0.25f;

        [Header("Skill 1 — Synergy AoE (Guardian heal demo, Module 3)")]
        [Tooltip("Networked AoE prefab (AreaOfEffectNetworked + NetworkObject), registered in Network Prefabs.")]
        [SerializeField] private NetworkObject synergyAoePrefab;
        [SerializeField] private float skill1Cooldown = 8f;
        [SerializeField] private float skill1CastRange = 6f;

        public Vector3 AimPoint { get; private set; }
        public Vector3 AimDirection { get; private set; } = Vector3.forward;

        /// <summary>GS-17 §6.4 — kit runtimes (AP's Ultimate) flip this; APBasicAttackController gates hold-to-beam on it.</summary>
        public bool IsInUltimateMode { get; set; }

        // Facing replication: owner computes the sector and writes; everyone else reads.
        // 1 byte, delta-synced only on sector change — vastly cheaper than syncing the aim vector.
        private readonly NetworkVariable<byte> _netFacing = new(
            (byte)FacingDirection8.S,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        // GS-17 §4 — cosmetic aim replication for the weapon rig: one byte, ~1.4°
        // resolution (256 steps / 360°), gated by BOTH a 3° movement threshold and a
        // ~14 Hz rate cap. Effectively zero traffic while holding still.
        private readonly NetworkVariable<byte> _netAimAngle = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private const float AimSendThresholdDeg = 3f;
        private const float AimSendInterval = 1f / 14f;
        private float _lastSentAimAngle;
        private float _nextAimSendTime;

        // GS-17 §6.2 — the basic attack behaviour split. Discovered in Awake();
        // ComposedBasicAttackBehaviour for the 6 generic heroes, bespoke controllers
        // for the Gladiators. HeroController's call sites are IDENTICAL for both.
        private IBasicAttackBehaviour _basicAttack;
        private IHoldableBasicAttack _holdableAttack;
        // Commander archetype hook (BahadirRollController etc.) — optional, most heroes
        // have no component implementing this and that's fine (see IRollBehaviour).
        private IRollBehaviour _rollBehaviour;

        // Tap/hold input state (owner-only).
        private bool _attackPressed;
        private bool _holdBegan;
        private float _pressStartTime;

        private CharacterController _controller;
        private AbilityController _abilities; // Optional sibling (GS-9); null-safe everywhere.
        private StatusEffectController _status; // Optional sibling (GS-5); null-safe everywhere.
        private Vector3 _moveInputWorld;
        private float _verticalVelocity;
        private float _rollTimeRemaining;
        private Vector3 _rollDirection;

        // Cooldown gates: the local one is UX (don't spam RPCs); the SERVER one is law.
        private float _localNextAttackTime;
        private float _serverNextAttackTime;
        private float _localNextSkill1Time;
        private float _serverNextSkill1Time;

        // Generated by the Input Actions asset (Assets/InputSystem_Actions.inputactions,
        // "Generate C# Class" -> InputSystem_Actions.cs). Replaces direct Keyboard.current /
        // Mouse.current polling with the "Player" action map (Move, Aim, Attack, Roll, Skill1).
        private InputSystem_Actions _input;

        protected override void Awake()
        {
            base.Awake();
            _controller = GetComponent<CharacterController>();
            _abilities = GetComponent<AbilityController>();
            _status = GetComponent<StatusEffectController>();
            if (isoCamera == null) isoCamera = Camera.main;

            // GS-17 §6.2: no Inspector dropdown — the correct behaviour is whichever
            // component sits on this prefab.
            _basicAttack = GetComponent<IBasicAttackBehaviour>();
            _holdableAttack = _basicAttack as IHoldableBasicAttack;
            if (_basicAttack == null)
                Debug.LogWarning($"[HeroController] {name} has no IBasicAttackBehaviour component — basic attacks will do nothing. Add ComposedBasicAttackBehaviour (generic heroes) or a bespoke controller (Gladiators).", this);

            // Optional — unlike basic attack, most heroes don't modify Roll, so no warning.
            _rollBehaviour = GetComponent<IRollBehaviour>();

            if (weaponVisual == null) weaponVisual = GetComponentInChildren<WeaponVisualController>();

            _input = new InputSystem_Actions();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Remote proxies: no input, no CharacterController physics — ClientNetworkTransform
            // moves them. Facing arrives through the NetworkVariable callback below.
            _controller.enabled = IsOwner;
            enabled = IsOwner || IsServer; // Server keeps Update alive only for its own guards; input is gated by IsOwner.

            _netFacing.OnValueChanged += HandleFacingChanged;
            _netAimAngle.OnValueChanged += HandleAimAngleChanged;

            // OnValueChanged does NOT fire for the initial spawn sync — apply the current
            // value manually so late joiners see remote heroes facing/aiming the right way.
            if (!IsOwner)
            {
                if (spriteDirection != null)
                    spriteDirection.SetDirection((FacingDirection8)_netFacing.Value);
                ApplyRemoteAim(_netAimAngle.Value);
            }

            // GS-17 §2: BasicAttack rides the same CooldownManager as every other slot.
            // Duration is re-read from Stats on EVERY trigger (attack-speed buffs apply
            // attack-by-attack for free) — the registration just creates the clock.
            if (IsServer && _abilities != null)
                _abilities.Cooldowns.RegisterSlot(AbilitySlot.BasicAttack);

            // Only the owning client should ever read local hardware input — remote proxies
            // and the dedicated server never enable the map.
            if (IsOwner)
            {
                _input.Player.Enable();

                // Named handlers (not lambdas) — a `ctx => Foo()` lambda in OnNetworkDespawn
                // creates a NEW delegate instance, so `-=` against it silently fails to
                // unsubscribe and leaks the old handler across spawn/despawn cycles.
                // GS-17 tap/hold: press = started, release = canceled — the hold branch
                // needs both edges, `performed` alone only gives one.
                _input.Player.Attack.started += OnAttackStarted;
                _input.Player.Attack.canceled += OnAttackCanceled;
                _input.Player.Roll.performed += OnRollPerformed;
                _input.Player.Skill1.performed += OnSkill1Performed;
                _input.Player.Skill2.performed += OnSkill2Performed;
                _input.Player.Ultimate.performed += OnUltimatePerformed;
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            _netFacing.OnValueChanged -= HandleFacingChanged;
            _netAimAngle.OnValueChanged -= HandleAimAngleChanged;

            if (IsOwner)
            {
                _input.Player.Attack.started -= OnAttackStarted;
                _input.Player.Attack.canceled -= OnAttackCanceled;
                _input.Player.Roll.performed -= OnRollPerformed;
                _input.Player.Skill1.performed -= OnSkill1Performed;
                _input.Player.Skill2.performed -= OnSkill2Performed;
                _input.Player.Ultimate.performed -= OnUltimatePerformed;
                _input.Player.Disable();
            }
        }

        private void OnDestroy()
        {
            // Generated input action classes hold native (unmanaged) resources — dispose or
            // they leak for the lifetime of the process, not just the scene.
            _input?.Dispose();
        }

        private void HandleFacingChanged(byte previous, byte current)
        {
            // Owner already applied its facing locally this frame — only remotes react here.
            if (IsOwner || spriteDirection == null) return;
            spriteDirection.SetDirection((FacingDirection8)current);
        }

        // GS-17 §4 — remote clients reconstruct the aim vector from the throttled byte.
        // Done in the callback (not Update) because remote proxies run with this
        // component disabled; WeaponVisualController smooths the ~1.4° steps itself.
        private void HandleAimAngleChanged(byte previous, byte current)
        {
            if (IsOwner) return;
            ApplyRemoteAim(current);
        }

        private void ApplyRemoteAim(byte quantized)
        {
            float rad = quantized * (360f / 256f) * Mathf.Deg2Rad;
            AimDirection = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner || !IsAlive) return;

            ReadMoveInput();
            UpdateAim();
            SendAimIfNeeded();
            UpdateHold();
            ApplyMovement();
            UpdateFacing();
            // Attack / Roll / Skill1 no longer polled here — they arrive as input
            // callbacks (OnAttackStarted/Canceled / OnRollPerformed / OnSkill1Performed).
        }

        // GS-17 §4 — owner-side write gate: BOTH a 3° threshold AND a ~14 Hz rate cap.
        private void SendAimIfNeeded()
        {
            float angle = Mathf.Atan2(AimDirection.z, AimDirection.x) * Mathf.Rad2Deg;
            if (Mathf.Abs(Mathf.DeltaAngle(angle, _lastSentAimAngle)) < AimSendThresholdDeg) return;
            if (Time.time < _nextAimSendTime) return;

            _lastSentAimAngle = angle;
            _nextAimSendTime = Time.time + AimSendInterval;
            _netAimAngle.Value = (byte)Mathf.RoundToInt(Mathf.Repeat(angle, 360f) / 360f * 256f);
        }

        // GS-17 §6.3 — tap-vs-hold branch. The press waits holdThreshold before deciding:
        // firing immediately on press would be wrong ("tap does A, hold does B").
        private void UpdateHold()
        {
            if (!_attackPressed) return;

            if (!_holdBegan && Time.time - _pressStartTime >= holdThreshold)
            {
                _holdBegan = true;
                _holdableAttack.OnHoldBegin(this, AimPoint);
            }

            if (_holdBegan)
                _holdableAttack.OnHoldUpdate(this, AimPoint);
        }

        // ---------------------------------------------------------------- Movement (OWNER)

        private void ReadMoveInput()
        {
            // The Input System resolves WASD/arrows/gamepad stick into one Vector2 itself —
            // no null-device checks needed, ReadValue<T>() just returns default when nothing
            // is connected.
            Vector2 raw = _input.Player.Move.ReadValue<Vector2>();

            if (raw.sqrMagnitude < 0.01f) { _moveInputWorld = Vector3.zero; return; }

            // Camera-relative isometric basis (flatten + normalize compensates the tilt).
            Transform cam = isoCamera.transform;
            Vector3 camForward = cam.forward; camForward.y = 0f; camForward.Normalize();
            Vector3 camRight   = cam.right;   camRight.y   = 0f; camRight.Normalize();

            _moveInputWorld = (camForward * raw.y + camRight * raw.x).normalized;
        }

        private void ApplyMovement()
        {
            _verticalVelocity = _controller.isGrounded ? -1f : _verticalVelocity + Gravity * Time.deltaTime;

            Vector3 horizontal;
            // GS-5/GS-9: hard-root/stun effects (Bahadır Ultimate's self-channel-lock) block
            // owner-authoritative movement too, not just server-side ability gating.
            bool canMove = _status == null || _status.CanMove;

            if (!canMove)
            {
                horizontal = Vector3.zero;
            }
            else if (_rollTimeRemaining > 0f)
            {
                _rollTimeRemaining -= Time.deltaTime;
                horizontal = _rollDirection * Stats.GetStat(StatType.RollSpeed);

                // Fires the frame the roll's duration elapses — after this frame's roll
                // movement is still applied below, so the dash completes before the hook runs.
                if (_rollTimeRemaining <= 0f)
                    _rollBehaviour?.OnRollEnd(this);
            }
            else
            {
                // Two independent, stacking speed sources, both server-authoritative:
                // SpeedMultiplier = BaseHero's own NetworkVariable (Module-3 aura buffs,
                // ServerApplySpeedBuff). _status.MoveSpeedMultiplier = GS-5.2 status effects
                // (e.g. Bahadır Feature's Fx_SpeedBuff, moveSpeedMultiplier: 1.3) — this was
                // previously never read here, so status-effect speed buffs/slows silently
                // did nothing to movement.
                float statusSpeed = _status != null ? _status.MoveSpeedMultiplier : 1f;
                horizontal = _moveInputWorld * (Stats.GetStat(StatType.MoveSpeed) * SpeedMultiplier * statusSpeed);
            }

            _controller.Move((horizontal + Vector3.up * _verticalVelocity) * Time.deltaTime);
        }

        public override void PerformRoll(Vector3 direction)
        {
            if (_rollTimeRemaining > 0f) return;
            _rollDirection = direction.sqrMagnitude > 0.01f ? direction.normalized : AimDirection;
            _rollTimeRemaining = rollDuration;
            // Roll is owner-authoritative movement — local-only log, zero network traffic.
            CombatLogManager.LogLocal(DisplayName, "used", "Roll", transform.position);

            _rollBehaviour?.OnRollStart(this, _rollDirection);
        }

        // ---------------------------------------------------------------- Aiming & Facing (OWNER)

        private void UpdateAim()
        {
            if (isoCamera == null) return;

            // "Aim" is bound to <Mouse>/position (absolute screen point), not a delta —
            // required for ScreenPointToRay to project a stable world-space cursor.
            Vector2 screenPos = _input.Player.Aim.ReadValue<Vector2>();

            // Mathematical plane at feet height — infinite, collider-free cursor projection.
            Ray ray = isoCamera.ScreenPointToRay(screenPos);
            var groundPlane = new Plane(Vector3.up, new Vector3(0f, transform.position.y, 0f));

            if (groundPlane.Raycast(ray, out float enter))
            {
                AimPoint = ray.GetPoint(enter);
                Vector3 toAim = AimPoint - transform.position;
                toAim.y = 0f;
                if (toAim.sqrMagnitude > 0.01f) AimDirection = toAim.normalized;
            }
        }

        private void UpdateFacing()
        {
            if (spriteDirection == null) return;
            spriteDirection.SetFacing(_moveInputWorld);           // Immediate local visual based on movement.
            byte sector = (byte)spriteDirection.CurrentDirection; // Replicate the RESULT (1 byte),
            if (_netFacing.Value != sector) _netFacing.Value = sector; // not the input vector.
        }

        // ---------------------------------------------------------------- Combat Input Events (OWNER)
        // GS-17 tap/hold: `started` = button down, `canceled` = button up. Heroes whose
        // behaviour isn't holdable (or whose hold branch is gated off this frame, e.g.
        // AP outside Ultimate Mode) fire the tap immediately on press — identical feel
        // to the old click-per-swing input.

        private void OnAttackStarted(InputAction.CallbackContext ctx)
        {
            if (_basicAttack == null) return;

            if (_holdableAttack != null && _holdableAttack.HoldEnabled(this))
            {
                // Tap-vs-hold ambiguity: wait for threshold (UpdateHold) or release.
                _attackPressed = true;
                _holdBegan = false;
                _pressStartTime = Time.time;
                return;
            }

            TryPerformBasicAttack();
        }

        private void OnAttackCanceled(InputAction.CallbackContext ctx)
        {
            if (!_attackPressed) return;
            _attackPressed = false;

            if (_holdBegan)
            {
                _holdBegan = false;
                _holdableAttack.OnHoldRelease(this);
            }
            else
            {
                TryPerformBasicAttack(); // released before threshold = tap
            }
        }

        /// <summary>
        /// GS-17 §6.2 — THE shared owner-side entry point for both tracks (data-driven
        /// and bespoke). Local gate is UX only; the server-side gate in AttackServerRpc
        /// is law.
        /// </summary>
        public void TryPerformBasicAttack()
        {
            if (Time.time < _localNextAttackTime) return;
            _localNextAttackTime = Time.time + Stats.GetStat(StatType.AttackCooldown);
            AttackServerRpc(AimPoint); // Request — the server decides if it actually happens.
        }

        private void OnRollPerformed(InputAction.CallbackContext ctx)
        {
            PerformRoll(_moveInputWorld);
        }

        private void OnSkill1Performed(InputAction.CallbackContext ctx)
        {
            // GS-9 path: if an AbilityDataSO is assigned to the Skill1 slot, the ability
            // system owns this input (server-side cooldown/silence validation + targeting).
            if (_abilities != null && _abilities.HasSlotAssigned(AbilitySlot.Skill1))
            {
                _abilities.TryActivate(AbilitySlot.Skill1, AimPoint);
                return;
            }

            // Legacy Module-3 demo path (synergy AoE prefab) — kept until kits migrate.
            if (Time.time >= _localNextSkill1Time)
            {
                _localNextSkill1Time = Time.time + skill1Cooldown;
                CastSynergyServerRpc(AimPoint);
            }
        }

        // GS-9 path only — Skill2/Ultimate never had a legacy hardcoded implementation,
        // so there's nothing to fall back to. If no AbilityDataSO is assigned to the slot
        // (AbilityController component, Inspector), this is a silent no-op: HasSlotAssigned
        // reads the serialized field directly, so it's accurate even before the server
        // finishes building runtimes.
        private void OnSkill2Performed(InputAction.CallbackContext ctx)
        {
            if (_abilities != null && _abilities.HasSlotAssigned(AbilitySlot.Skill2))
                _abilities.TryActivate(AbilitySlot.Skill2, AimPoint);
        }

        private void OnUltimatePerformed(InputAction.CallbackContext ctx)
        {
            if (_abilities != null && _abilities.HasSlotAssigned(AbilitySlot.Ultimate))
                _abilities.TryActivate(AbilitySlot.Ultimate, AimPoint);
        }

        /// <summary>
        /// [ServerRpc]: owner -> server. Default RequireOwnership=true means NGO itself
        /// rejects this RPC from anyone but the owner — free anti-spoofing.
        /// </summary>
        [ServerRpc]
        private void AttackServerRpc(Vector3 aimPoint)
        {
            if (!IsAlive) return;

            // GS-17 §2 — server-authoritative cooldown through the SAME CooldownManager
            // as Skill1/2/Ultimate. The duration is re-read from Stats on every trigger,
            // so attack-speed buffs/debuffs apply the instant they change — no separate
            // "recalculate basic attack cooldown" system anywhere.
            // 0.95 tolerance absorbs clock jitter against the client's optimistic gate.
            float attackCooldown = Stats.GetStat(StatType.AttackCooldown);

            if (_abilities != null)
            {
                if (!_abilities.Cooldowns.IsReady(AbilitySlot.BasicAttack)) return;
                _abilities.Cooldowns.Commit(AbilitySlot.BasicAttack, attackCooldown * 0.95f);
            }
            else
            {
                // Fallback for prefabs without an AbilityController (shouldn't exist post-GS-9).
                if (Time.time < _serverNextAttackTime) return;
                _serverNextAttackTime = Time.time + attackCooldown * 0.95f;
            }

            PerformBasicAttack(aimPoint);
        }

        /// <summary>
        /// SERVER-side. GS-17 §6.2: the old hardcoded OverlapSphere melee is gone — the
        /// behaviour component (data-driven SO wrapper or bespoke Gladiator controller)
        /// owns target acquisition through the shared Delivery pipeline.
        /// </summary>
        public override void PerformBasicAttack(Vector3 aimPoint)
        {
            if (!IsServer || _basicAttack == null) return;

            Vector3 dir = aimPoint - transform.position;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;

            _basicAttack.Fire(this, aimPoint);

            CombatLogManager.LogAction(DisplayName, "used", "Basic_Attack", transform.position);
            AttackSwingClientRpc(dir);
        }

        /// <summary>
        /// [ClientRpc]: server -> all clients. Presentation only, never game state.
        /// GS-17 §3: weapon recoil triggers HERE so every client (including the owner)
        /// plays it in sync with the confirmed attack, not with local input prediction.
        /// </summary>
        [ClientRpc]
        private void AttackSwingClientRpc(Vector3 direction)
        {
            if (weaponVisual != null) weaponVisual.PlayRecoil(direction);
            // Hook: swing animation / whiff-vs-hit audio. Impact juice (hitstop, shake,
            // flash) is owned by the VICTIM's hit-reaction RPC so it triggers exactly once.
        }

        // ---------------------------------------------------------------- Skill 1: Synergy cast (Module 3)

        [ServerRpc]
        private void CastSynergyServerRpc(Vector3 targetPoint)
        {
            if (!IsAlive || synergyAoePrefab == null) return;
            if (Time.time < _serverNextSkill1Time) return;
            _serverNextSkill1Time = Time.time + skill1Cooldown * 0.95f;

            // Server clamps the cast point into range — the client's click is a SUGGESTION.
            Vector3 toTarget = targetPoint - transform.position;
            toTarget.y = 0f;
            Vector3 castPos = transform.position + Vector3.ClampMagnitude(toTarget, skill1CastRange);

            NetworkObject aoe = Instantiate(synergyAoePrefab, castPos, Quaternion.identity);
            if (aoe.TryGetComponent(out CBuilding.Combat.AreaOfEffectNetworked effect))
                effect.Initialize(DisplayName);
            aoe.Spawn(true); // Replicates to every client; visuals on the prefab appear everywhere.

            CombatLogManager.LogAction(DisplayName, "casted", "Skill_1", castPos);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (Stats == null || Stats.BaseStats == null) return;
            // Basic attack range/shape now lives in the assigned SO's Delivery config
            // (GS-17 §2) — this just shows the AttackRange stat as an aim reference.
            Gizmos.color = Color.red;
            Gizmos.DrawRay(transform.position, AimDirection * Stats.BaseStats.AttackRange);
        }
#endif
    }
}
