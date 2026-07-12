using System.Collections.Generic;
using CBuilding.Abilities;
using CBuilding.Abilities.Delivery;
using CBuilding.Core;
using CBuilding.Enemies;
using CBuilding.StatusEffects;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace CBuilding.Heroes
{
    /// <summary>
    /// GS-17 §6.3 — Kerem's bespoke basic attack, "Show Your Weakness".
    ///
    /// TAP: 3 real Projectile-Delivery instances in a fixed triangular spread — the
    /// spread ability asset does this (Projectile delivery, count 3, spreadAngle ~16°,
    /// pierceCount 2 = "pierce one, dissipate on the 2nd hit"). Effects on the asset:
    /// Damage + ApplyStatusEffect(StackingMark, 4 stacks). Going through ExecuteDelivery
    /// keeps crit/label bonuses/Spyware interactions consistent (GS-9.4 standing rule).
    ///
    /// HOLD: telekinesis — grabs every FULLY-MARKED enemy near the cursor, drags them
    /// while held, slams on release. Grab set is LOCKED at grab time (rec #6): a target
    /// dying mid-drag just skips the slam; a shielded one eats it normally through the
    /// shield. Drag position syncs with the §4 compression pattern (threshold + rate
    /// cap), not a new strategy.
    ///
    /// PREFAB: on Kerem's hero root, next to HeroController + AbilityController.
    /// </summary>
    public class KeremBasicAttackController : NetworkBehaviour, IBasicAttackBehaviour, IHoldableBasicAttack
    {
        [Header("Tap — triangular spread (wire the ComposedAbilitySO)")]
        [Tooltip("Projectile delivery: count 3, spreadAngle ~16°, pierceCount 2. Effects: Damage + ApplyStatusEffect(Fx_StackingMark_Kerem).")]
        [SerializeField] private ComposedAbilitySO spreadAbility;

        [Header("Hold — telekinesis")]
        [Tooltip("Grab radius around the cursor at hold-begin.")]
        [SerializeField] private float grabRadius = 3.5f;
        [Tooltip("Max simultaneous grabbed enemies (sanity/net cap).")]
        [SerializeField] private int maxGrabbed = 6;
        [Tooltip("How fast dragged enemies chase the cursor (m/s-ish lerp factor).")]
        [SerializeField] private float dragLerpSpeed = 10f;
        [SerializeField] private float floatHeight = 1.2f;
        [Tooltip("Optional: a Stun-flag EffectDataSO applied while grabbed so AI/movement stops fighting the drag.")]
        [SerializeField] private EffectDataSO grabbedEffect;

        [Header("Slam (on release)")]
        [SerializeField] private float slamDamage = 30f;
        [SerializeField] private float slamKnockback = 8f;
        [Tooltip("Marks are consumed by the slam (segments reset).")]
        [SerializeField] private bool slamConsumesMark = true;
        [SerializeField] private EffectDataSO stackingMarkEffect; // the same asset the spread applies — for removal

        // §4 compression pattern reused for the 2D drag point: threshold + rate cap.
        private const float DragSendThreshold = 0.15f;
        private const float DragSendInterval = 1f / 14f;
        private Vector3 _lastSentDragPoint;
        private float _nextDragSendTime;

        // SERVER state — the locked grab set (rec #6).
        private readonly List<BaseEnemy> _grabbed = new();
        private readonly List<Vector3> _grabOffsets = new();
        private Vector3 _dragPoint;
        private bool _isGrabbing;

        // GS-16 integration point: grabbed ids replicate so every client can play the
        // floating/tether VFX. Movement itself already replicates via the enemies'
        // server-auth NetworkTransforms — this list is presentation metadata only.
        private readonly NetworkList<ulong> _grabbedIds = new(
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);

        public NetworkList<ulong> GrabbedIds => _grabbedIds;

        private AbilityController _abilities;

        private void Awake()
        {
            _abilities = GetComponent<AbilityController>();
            if (spreadAbility == null)
                Debug.LogWarning("[KeremBasicAttackController] No spreadAbility assigned — tap attack will do nothing.", this);
        }

        // ---- IBasicAttackBehaviour (SERVER, via HeroController's validated path) ----

        public void Fire(HeroController hero, Vector3 aimPoint)
        {
            if (spreadAbility == null || _abilities == null) return;
            spreadAbility.ExecuteDelivery(_abilities, aimPoint);
        }

        // ---- IHoldableBasicAttack (OWNER side) ----

        public bool HoldEnabled(HeroController hero) => true; // tap-vs-hold is Kerem's whole kit

        public void OnHoldBegin(HeroController hero, Vector3 aimPoint)
        {
            _lastSentDragPoint = aimPoint;
            _nextDragSendTime = 0f;
            BeginTelekinesisServerRpc(aimPoint);
        }

        public void OnHoldUpdate(HeroController hero, Vector3 currentWorldPoint)
        {
            // Same gate as the 1-byte aim angle (§4): only send when it moved enough,
            // never faster than ~14 Hz.
            if ((currentWorldPoint - _lastSentDragPoint).sqrMagnitude < DragSendThreshold * DragSendThreshold) return;
            if (Time.time < _nextDragSendTime) return;

            _lastSentDragPoint = currentWorldPoint;
            _nextDragSendTime = Time.time + DragSendInterval;
            UpdateDragServerRpc(currentWorldPoint);
        }

        public void OnHoldRelease(HeroController hero)
        {
            ReleaseTelekinesisServerRpc();
        }

        // ---- SERVER: telekinesis state machine ----

        [ServerRpc]
        private void BeginTelekinesisServerRpc(Vector3 aimPoint)
        {
            if (_isGrabbing) return;

            var hero = GetComponent<HeroController>();
            if (hero == null || !hero.IsAlive) return;

            _dragPoint = aimPoint;
            _grabbed.Clear();
            _grabOffsets.Clear();
            _grabbedIds.Clear();

            // Grab-time lock (rec #6): validate the fully-marked set ONCE, here.
            foreach (BaseEnemy enemy in EnemyRegistry.GetAllWithEffect<StackingMarkStatus>())
            {
                if (_grabbed.Count >= maxGrabbed) break;
                if ((enemy.transform.position - aimPoint).sqrMagnitude > grabRadius * grabRadius) continue;

                var status = enemy.GetComponent<StatusEffectController>();
                var mark = status != null ? status.GetActiveEffectOfType<StackingMarkStatus>() : null;
                if (mark == null || !mark.IsFullyStacked || !mark.IsFrom(gameObject)) continue;

                _grabbed.Add(enemy);
                _grabOffsets.Add(enemy.transform.position - aimPoint);

                if (grabbedEffect != null && status != null) status.ApplyEffect(grabbedEffect, gameObject);
                if (enemy.TryGetComponent<NavMeshAgent>(out var agent)) agent.enabled = false;
                if (enemy.TryGetComponent<NetworkObject>(out var netObj)) _grabbedIds.Add(netObj.NetworkObjectId);
            }

            _isGrabbing = _grabbed.Count > 0;
        }

        [ServerRpc]
        private void UpdateDragServerRpc(Vector3 dragPoint)
        {
            if (_isGrabbing) _dragPoint = dragPoint;
        }

        private void Update()
        {
            if (!IsServer || !_isGrabbing) return;

            // Dead targets are simply skipped (rec #6) — the set itself stays locked.
            for (int i = 0; i < _grabbed.Count; i++)
            {
                BaseEnemy enemy = _grabbed[i];
                if (enemy == null || !enemy.IsAlive) continue;

                Vector3 goal = _dragPoint + _grabOffsets[i];
                goal.y = floatHeight;
                enemy.transform.position = Vector3.Lerp(
                    enemy.transform.position, goal, dragLerpSpeed * Time.deltaTime);
            }
        }

        [ServerRpc]
        private void ReleaseTelekinesisServerRpc()
        {
            if (!_isGrabbing) return;
            _isGrabbing = false;

            for (int i = 0; i < _grabbed.Count; i++)
            {
                BaseEnemy enemy = _grabbed[i];
                if (enemy == null) continue;

                var status = enemy.GetComponent<StatusEffectController>();
                if (grabbedEffect != null && status != null) status.RemoveEffect(grabbedEffect);

                // Ground + restore AI regardless of slam outcome.
                Vector3 ground = enemy.transform.position; ground.y = 0f;
                enemy.transform.position = ground;
                if (enemy.TryGetComponent<NavMeshAgent>(out var agent)) agent.enabled = true;

                // Rec #6 edge resolution AT SLAM TIME: dead target just skips; a
                // shielded target eats the slam normally through the shield (TakeDamage
                // runs the full modifier pipeline — Kerem's own mark amplifies it too).
                if (!enemy.IsAlive) continue;

                Vector3 outward = enemy.transform.position - transform.position;
                enemy.TakeDamage(new DamageInfo(
                    slamDamage, enemy.transform.position, outward, slamKnockback,
                    gameObject, DamageFlags.Ability));

                if (slamConsumesMark && stackingMarkEffect != null && status != null)
                    status.RemoveEffect(stackingMarkEffect);
            }

            _grabbed.Clear();
            _grabOffsets.Clear();
            _grabbedIds.Clear();

            CombatLogManager.LogAction(GetComponent<HeroController>()?.DisplayName ?? name,
                "used", "Telekinesis_Slam", _dragPoint);
        }
    }
}
