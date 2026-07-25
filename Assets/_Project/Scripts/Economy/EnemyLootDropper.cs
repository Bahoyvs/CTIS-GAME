using CBuilding.Enemies;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Economy
{
    /// <summary>
    /// SERVER-ONLY loot spawner for enemy prefabs. Sits next to BaseEnemy.
    ///
    /// Auto-hooks BaseEnemy.OnDied (a server-side event) so kills drop loot with zero glue
    /// code; ServerDropLoot() stays public for scripted deaths (RebirthEgg, FissionOnDeath
    /// style interceptors) that bypass the normal death path.
    ///
    /// The point roll is split into chunks of pointsPerPickup so a fat roll scatters as
    /// several small drops — reads better in co-op (everyone sees the shower) and each
    /// chunk resolves pickup ownership independently on the server.
    ///
    /// PHYSICS FEEL: impulses are applied server-side right after Spawn. Clients never
    /// simulate this — NetworkRigidbody keeps their bodies kinematic and they just render
    /// the replicated NetworkTransform, so the arc is identical on every screen.
    /// </summary>
    [RequireComponent(typeof(BaseEnemy))]
    public class EnemyLootDropper : NetworkBehaviour
    {
        [Header("Loot (server-rolled)")]
        [Tooltip("Registered network prefab with PixelPointPickup on the root.")]
        [SerializeField] private PixelPointPickup pickupPrefab;

        [SerializeField, Min(0)] private int minPoints = 5;
        [SerializeField, Min(0)] private int maxPoints = 15;

        [Tooltip("Roll is split into pickups worth at most this much each.")]
        [SerializeField, Min(1)] private int pointsPerPickup = 5;

        [Header("Pop Physics (isometric juice)")]
        [Tooltip("Spawn height above the enemy pivot so drops don't clip the floor.")]
        [SerializeField, Min(0f)] private float spawnHeight = 0.5f;

        [SerializeField, Min(0f)] private float upwardForceMin = 2.5f;
        [SerializeField, Min(0f)] private float upwardForceMax = 4f;

        [Tooltip("Horizontal scatter impulse — keep small on a 1x1 grid so loot stays near the corpse.")]
        [SerializeField, Min(0f)] private float outwardForceMin = 0.5f;
        [SerializeField, Min(0f)] private float outwardForceMax = 1.5f;

        [SerializeField, Min(0f)] private float spinTorque = 2f;

        private BaseEnemy _enemy;
        private bool _dropped; // Latch: OnDied + a manual ServerDropLoot call must not double-pay.

        private void Awake() => _enemy = GetComponent<BaseEnemy>();

        public override void OnNetworkSpawn()
        {
            if (IsServer) _enemy.OnDied += HandleDied;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) _enemy.OnDied -= HandleDied;
            _dropped = false; // Reset for NetworkEnemyPool reuse.
        }

        private void HandleDied(BaseEnemy enemy) => ServerDropLoot();

        /// <summary>SERVER-ONLY. Roll points, spawn pickups, pop them outward.</summary>
        public void ServerDropLoot()
        {
            if (!IsServer || _dropped || pickupPrefab == null) return;
            _dropped = true;

            // int Random.Range max is EXCLUSIVE — the classic off-by-one, hence the +1.
            int remaining = Random.Range(minPoints, maxPoints + 1);
            Vector3 origin = transform.position + Vector3.up * spawnHeight;

            while (remaining > 0)
            {
                int chunk = Mathf.Min(pointsPerPickup, remaining);
                remaining -= chunk;
                SpawnPickup(origin, chunk);
            }
        }

        private void SpawnPickup(Vector3 origin, int value)
        {
            PixelPointPickup pickup = Instantiate(pickupPrefab, origin, Quaternion.identity);

            // Order matters: value before Spawn (plain server-side field, no replication
            // needed), forces after Spawn (Rigidbody is live and server-authoritative).
            pickup.ServerSetValue(value);
            pickup.NetworkObject.Spawn(true);

            var rb = pickup.GetComponent<Rigidbody>();
            Vector2 dir = Random.insideUnitCircle.normalized;
            float outward = Random.Range(outwardForceMin, outwardForceMax);
            float upward = Random.Range(upwardForceMin, upwardForceMax);

            // Impulse = instant velocity kick, mass-scaled — the "pop". A per-frame Force
            // would need sustained application; wrong tool for a one-shot burst.
            rb.AddForce(new Vector3(dir.x * outward, upward, dir.y * outward), ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * spinTorque, ForceMode.Impulse);
        }
    }
}
