using Unity.Netcode;
using UnityEngine;
using CBuilding.Enemies.Spawning;

namespace CBuilding.Enemies.Roster
{
    /// <summary>
    /// The Greedy / The Contented set-piece pairing: whenever this enemy spawns, its
    /// partner spawns beside it through SpawnDirector.ServerSpawnAt — so the pair always
    /// arrives together and BOTH count against Threat Capacity. Put this on ONE of the
    /// two only (Greedy), or they'll chain-spawn forever. The partner prefab must be
    /// prewarmed in NetworkEnemyPool (extraPrefabs) and must NOT sit in any encounter
    /// pool on its own — per the roster doc, neither is independently seedable.
    /// </summary>
    public class PairedCoSpawn : NetworkBehaviour
    {
        [Tooltip("Partner spawned next to this enemy (Greedy -> Contented).")]
        [SerializeField] private BaseEnemy partnerPrefab;

        [Min(0.5f)] [SerializeField] private float spawnOffset = 1.8f;

        [Tooltip("Threat Capacity the partner occupies (it bypasses the encounter pool).")]
        [Min(0f)] [SerializeField] private float partnerThreatCost = 6f;

        public override void OnNetworkSpawn()
        {
            if (!IsServer || partnerPrefab == null) return;

            if (SpawnDirector.Instance == null)
            {
                Debug.LogWarning("[PairedCoSpawn] No SpawnDirector — partner not spawned.", this);
                return;
            }

            Vector2 dir = Random.insideUnitCircle.normalized * spawnOffset;
            Vector3 pos = transform.position + new Vector3(dir.x, 0f, dir.y);
            SpawnDirector.Instance.ServerSpawnAt(partnerPrefab, pos, partnerThreatCost);
        }
    }
}
