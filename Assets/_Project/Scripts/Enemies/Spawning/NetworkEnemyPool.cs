using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Enemies.Spawning
{
    /// <summary>
    /// NGO-aware object pool for enemies. Mandatory for Fission Spawn (1→2→4) and
    /// Xenomorph Strain (clone-on-damage): raw Instantiate + Spawn churn would stall the
    /// server and flood clients with prefab instantiation spikes.
    ///
    /// HOW IT WORKS:
    ///   - Every pooled prefab gets an <see cref="INetworkPrefabInstanceHandler"/> registered
    ///     on ALL peers. From then on NGO delegates instance creation (clients) and instance
    ///     destruction (server + clients) for that prefab to this pool.
    ///   - Server: <see cref="ServerSpawn"/> pulls an inactive instance, places it, Spawn()s it.
    ///   - Death: BaseEnemy still calls NetworkObject.Despawn(true) — NGO routes the "destroy"
    ///     into <see cref="ReturnToPool"/> on every peer. No code change needed at call sites.
    ///
    /// SETUP: one instance in the gameplay scene (next to NetworkManager). Assign every
    /// SectionEncounterSO so prefabs are registered and prewarmed on host AND clients.
    /// </summary>
    public class NetworkEnemyPool : MonoBehaviour
    {
        public static NetworkEnemyPool Instance { get; private set; }

        [Tooltip("All section encounters — their pools are registered & prewarmed up front. " +
                 "Must be assigned on every build (host and clients) so client-side handlers exist.")]
        [SerializeField] private List<SectionEncounterSO> encounters = new();

        [Tooltip("Extra prefabs not present in any encounter (e.g. Micro-Spawn fission children).")]
        [SerializeField] private List<PrewarmEntry> extraPrefabs = new();

        [System.Serializable]
        public struct PrewarmEntry
        {
            public BaseEnemy prefab;
            [Min(0)] public int count;
        }

        private readonly Dictionary<GameObject, Queue<NetworkObject>> _pools = new();
        private readonly Dictionary<GameObject, GameObject> _sourcePrefabByInstance = new();

        // ------------------------------------------------------------------ Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[NetworkEnemyPool] Duplicate instance destroyed.", this);
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            // Handlers must exist on every peer BEFORE the first pooled spawn replicates.
            foreach (SectionEncounterSO encounter in encounters)
            {
                if (encounter == null) continue;
                foreach (EncounterEntry entry in encounter.AllEntries())
                {
                    if (entry.prefab != null) RegisterPrefab(entry.prefab.gameObject, entry.prewarmCount);
                }
            }
            foreach (PrewarmEntry extra in extraPrefabs)
            {
                if (extra.prefab != null) RegisterPrefab(extra.prefab.gameObject, extra.count);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            if (NetworkManager.Singleton != null)
            {
                foreach (GameObject prefab in _pools.Keys)
                    NetworkManager.Singleton.PrefabHandler.RemoveHandler(prefab);
            }
        }

        // ------------------------------------------------------------------ Public API

        /// <summary>Idempotent: registers the NGO handler and tops the pool up to 'prewarm'.</summary>
        public void RegisterPrefab(GameObject prefab, int prewarm)
        {
            if (!_pools.TryGetValue(prefab, out Queue<NetworkObject> pool))
            {
                pool = new Queue<NetworkObject>();
                _pools.Add(prefab, pool);

                if (NetworkManager.Singleton != null)
                    NetworkManager.Singleton.PrefabHandler.AddHandler(prefab, new PooledPrefabHandler(this, prefab));
                else
                    Debug.LogWarning("[NetworkEnemyPool] No NetworkManager — pool will run in local-only mode.", this);
            }

            while (pool.Count < prewarm)
                pool.Enqueue(CreateInactiveInstance(prefab));
        }

        /// <summary>
        /// Server-only. Pull → place → Spawn. Use this instead of Instantiate for every
        /// enemy (Director, SpawnNode, fission splits, xenomorph clones).
        /// </summary>
        public BaseEnemy ServerSpawn(BaseEnemy prefab, Vector3 position, Quaternion rotation)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                Debug.LogWarning("[NetworkEnemyPool] ServerSpawn called off-server.");
                return null;
            }

            NetworkObject instance = GetInstance(prefab.gameObject, position, rotation);
            instance.Spawn(true);
            return instance.GetComponent<BaseEnemy>();
        }

        // ------------------------------------------------------------------ Internals

        internal NetworkObject GetInstance(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (!_pools.TryGetValue(prefab, out Queue<NetworkObject> pool))
            {
                // Unregistered prefab reaching the pool: register lazily so it still works.
                RegisterPrefab(prefab, 0);
                pool = _pools[prefab];
            }

            NetworkObject instance = pool.Count > 0 ? pool.Dequeue() : CreateInactiveInstance(prefab);

            instance.transform.SetPositionAndRotation(position, rotation);
            instance.gameObject.SetActive(true);
            return instance;
        }

        internal void ReturnToPool(NetworkObject instance)
        {
            if (instance == null) return;

            instance.gameObject.SetActive(false);

            if (_sourcePrefabByInstance.TryGetValue(instance.gameObject, out GameObject prefab) &&
                _pools.TryGetValue(prefab, out Queue<NetworkObject> pool))
            {
                pool.Enqueue(instance);
            }
            else
            {
                Destroy(instance.gameObject); // Not ours — fail safe.
            }
        }

        private NetworkObject CreateInactiveInstance(GameObject prefab)
        {
            // Root-level on purpose: NGO forbids spawning a NetworkObject parented under a
            // plain GameObject, so pooled instances must not be children of the pool.
            GameObject go = Instantiate(prefab);
            go.SetActive(false);
            _sourcePrefabByInstance[go] = prefab;
            return go.GetComponent<NetworkObject>();
        }

        /// <summary>
        /// Bridges NGO instance lifecycle to the pool. Instantiate fires on CLIENTS when a
        /// pooled prefab spawn replicates; Destroy fires on ALL peers on despawn.
        /// </summary>
        private class PooledPrefabHandler : INetworkPrefabInstanceHandler
        {
            private readonly NetworkEnemyPool _pool;
            private readonly GameObject _prefab;

            public PooledPrefabHandler(NetworkEnemyPool pool, GameObject prefab)
            {
                _pool = pool;
                _prefab = prefab;
            }

            public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
                => _pool.GetInstance(_prefab, position, rotation);

            public void Destroy(NetworkObject networkObject)
                => _pool.ReturnToPool(networkObject);
        }
    }
}
