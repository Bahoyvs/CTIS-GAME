using CBuilding.Heroes;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Economy
{
    /// <summary>
    /// Dropped Pixel Point loot. Server-authoritative pickup — the anti-dupe pattern:
    ///
    ///   1. OnTriggerEnter fires on every peer that simulates physics, but ONLY the server
    ///      evaluates it (early-out otherwise). One authority = no double-claim when two
    ///      players touch the same drop on the same frame. Same idea as a unique-constraint
    ///      insert: whoever the server processes first wins, the row is gone for everyone else.
    ///   2. _consumed latches server-side because triggers can fire multiple times in the
    ///      frames before Despawn replicates.
    ///   3. Grant goes through PlayerEconomy.GrantPointsRpc (SendTo.Owner) — targeted at the
    ///      touching player's client only — then the server despawns + destroys the object.
    ///
    /// _pointValue is a plain int, not a NetworkVariable: only the server ever reads it,
    /// clients just render the mesh. Don't replicate state nobody reads.
    ///
    /// PREFAB SETUP: NetworkObject + Rigidbody + NetworkTransform + NetworkRigidbody,
    /// solid collider on root, trigger collider on child — see EnemyLootDropper header
    /// and the inspector notes that shipped with this script.
    /// </summary>
    [RequireComponent(typeof(NetworkObject), typeof(Rigidbody))]
    public class PixelPointPickup : NetworkBehaviour
    {
        [Header("Pickup")]
        [Tooltip("Grace period after spawn so the drop visibly 'pops' before it can be " +
                 "hoovered by whoever is standing in the corpse.")]
        [SerializeField, Min(0f)] private float armingTime = 0.35f;

        private int _pointValue = 1;  // Server-only state.
        private bool _consumed;       // Server-only latch.
        private float _spawnTime;

        public override void OnNetworkSpawn()
        {
            _spawnTime = Time.time;
        }

        /// <summary>SERVER-ONLY. Set by EnemyLootDropper after Instantiate, before Spawn.</summary>
        public void ServerSetValue(int value)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
            _pointValue = Mathf.Max(1, value);
        }

        private void OnTriggerEnter(Collider other)
        {
            // SERVER-ONLY evaluation — the whole point (see header).
            if (!IsServer || _consumed) return;
            if (Time.time < _spawnTime + armingTime) return;

            // GetComponentInParent: hero hitboxes can be child colliders under the
            // NetworkObject root (same lookup AbilityTargeting uses).
            var hero = other.GetComponentInParent<BaseHero>();
            if (hero == null || !hero.IsAlive) return;

            if (!hero.TryGetComponent<PlayerEconomy>(out var economy))
            {
                Debug.LogWarning($"[PixelPointPickup] {hero.name} has no PlayerEconomy — add it to the hero prefab.", hero);
                return;
            }

            _consumed = true;

            // Targeted grant: SendTo.Owner on the player's own PlayerEconomy resolves to
            // exactly that player's client. Then despawn for everyone (true = destroy).
            economy.GrantPointsRpc(_pointValue);
            NetworkObject.Despawn(true);
        }
    }
}
