using System.Collections.Generic;
using CBuilding.Enemies;
using CBuilding.StatusEffects;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Heroes.Bahadir
{
    /// <summary>
    /// "100% ghost effect" from the Feature design doc: while Stealth (ControlFlags.Stealth)
    /// is active, Bahadır's CharacterController has ZERO collision with enemies — not an
    /// approximation, actual Physics.IgnoreCollision between his controller and every
    /// enemy collider. Whoever he's currently overlapping gets stunned by the separate
    /// pass-through-stun tick (BahadirFeatureRuntime) — that part was already server-side
    /// and un-blocked by physical collision; this component is what makes "passing
    /// through" literally true instead of Bahadır bumping into enemies like a wall.
    ///
    /// WHY PER-COLLIDER IGNORE, NOT A LAYER-COLLISION-MATRIX EDIT: the matrix
    /// (ProjectSettings/DynamicsManager.asset) is global and permanent for every object
    /// on that layer — wrong tool for something that needs to turn on/off per-hero for a
    /// few seconds. Physics.IgnoreCollision scopes it to exactly this CharacterController
    /// vs exactly the enemies alive right now.
    ///
    /// WHY OWNER-ONLY: HeroController disables the CharacterController entirely on every
    /// peer except the owner (`_controller.enabled = IsOwner`) — remote copies never run
    /// Move(), so they can never physically collide with anything. There's nothing to
    /// ignore anywhere except the owner's own local physics world.
    ///
    /// NEW ENEMIES MID-STEALTH: no client-side "enemy spawned" event exists (EnemySpawnHooks
    /// is server-only), so this rescans the scene periodically while ghosting is active —
    /// same scene-scan trade-off EnemyRegistry already accepts for MVP-small enemy counts.
    /// </summary>
    [RequireComponent(typeof(HeroController), typeof(CharacterController))]
    public class BahadirGhostController : NetworkBehaviour
    {
        [Tooltip("How often (seconds) to rescan for enemies that spawned after Stealth began.")]
        [Min(0.05f)] [SerializeField] private float rescanInterval = 0.25f;

        private CharacterController _cc;
        private StatusEffectController _status;
        private readonly List<Collider> _ignoredColliders = new();
        private float _nextRescanTime;
        private bool _ghosting;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _status = GetComponent<StatusEffectController>();
        }

        public override void OnNetworkSpawn()
        {
            // Only the owner's local CharacterController can ever be physically blocked —
            // see class doc. Everyone else: disable Update entirely, nothing to do.
            if (!IsOwner || _status == null) { enabled = false; return; }
            _status.OnControlFlagsChanged += HandleControlFlagsChanged;
        }

        public override void OnNetworkDespawn()
        {
            if (_status != null) _status.OnControlFlagsChanged -= HandleControlFlagsChanged;
            RestoreAllCollisions();
        }

        private void HandleControlFlagsChanged(ControlFlags previous, ControlFlags current)
        {
            bool wasStealthed = (previous & ControlFlags.Stealth) != 0;
            bool isStealthed = (current & ControlFlags.Stealth) != 0;

            if (isStealthed && !wasStealthed) BeginGhosting();
            else if (!isStealthed && wasStealthed) EndGhosting();
        }

        private void BeginGhosting()
        {
            _ghosting = true;
            _nextRescanTime = 0f; // Rescan immediately this frame — don't wait a full interval.
        }

        private void EndGhosting()
        {
            _ghosting = false;
            RestoreAllCollisions();
        }

        private void Update()
        {
            if (!_ghosting || Time.time < _nextRescanTime) return;
            _nextRescanTime = Time.time + rescanInterval;
            IgnoreCollisionWithAllEnemies();
        }

        /// <summary>Ignores collision against every enemy collider not already ignored.</summary>
        private void IgnoreCollisionWithAllEnemies()
        {
            foreach (BaseEnemy enemy in FindObjectsByType<BaseEnemy>(FindObjectsSortMode.None))
            {
                if (enemy == null) continue;

                foreach (Collider col in enemy.GetComponentsInChildren<Collider>())
                {
                    if (col == null || _ignoredColliders.Contains(col)) continue;
                    Physics.IgnoreCollision(_cc, col, true);
                    _ignoredColliders.Add(col);
                }
            }
        }

        private void RestoreAllCollisions()
        {
            foreach (Collider col in _ignoredColliders)
            {
                // Enemy may have despawned mid-Stealth — Unity null-checks Destroyed objects
                // as == null, so this is a safe skip, not a silent leak.
                if (col != null) Physics.IgnoreCollision(_cc, col, false);
            }
            _ignoredColliders.Clear();
        }
    }
}
