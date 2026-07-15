using System.Collections.Generic;
using CBuilding.StatusEffects;
using CBuilding.Core;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Enemies.Spawning
{
    /// <summary>
    /// The AI Director. Server-only brain that decides WHAT spawns, WHERE and WHEN:
    ///
    ///   WHAT  — weighted pick from the current SectionEncounterSO, with event-modified
    ///           weights (Sandstorm → Bandits x N, NightPhase → night hunters x N) and a
    ///           Threat Capacity budget so the scene never over-fills.
    ///   WHERE — a SpawnNode whose type matches the enemy, that is off cooldown and inside
    ///           the pressure distance band. Optionally (UseFrustumCheck) also OUTSIDE every
    ///           player's reconstructed camera frustum; with the check off, on-screen spawns
    ///           are legal and the enemies' Spawning-state entry animation hides the pop.
    ///   WHEN  — a randomized pacing interval, plus an Attention meter that releases
    ///           special-pool enemies (Tribe Leader, Matriarch...) when it fills. Ability
    ///           usage feeds the meter via <see cref="ReportAttention"/> (multiplied at night).
    ///
    /// Extra behaviours, all data-driven from the SO:
    ///   - Mark of Guilt (House): a guilt-marked player gets a Family Echo from the node
    ///     NEAREST to them, on its own timer.
    ///   - Void events (Vacuum/DebrisShower): pure weight modifiers in the SO — big enemies
    ///     down-weighted, tactical drones up-weighted. No special-case code.
    ///
    /// SETUP: one instance per gameplay scene with a NetworkObject. Assign all
    /// SectionEncounterSOs; the director follows SectionManager.CurrentSection.
    /// </summary>
    public class SpawnDirector : NetworkBehaviour, ISpawnDirectorRouting
    {
        public static SpawnDirector Instance { get; private set; }

        [Header("Encounter Data")]
        [Tooltip("One asset per section; matched to SectionManager.CurrentSection via sectionIndex.")]
        [SerializeField] private List<SectionEncounterSO> encounters = new();

        [Header("Node Selection — distance")]
        [Tooltip("Never spawn closer than this to any player, even off-screen (no cheap shots).")]
        [Min(0f)] [SerializeField] private float minSpawnDistance = 6f;

        [Tooltip("Node must be within this range of at least one player, or the spawn is wasted.")]
        [Min(1f)] [SerializeField] private float maxSpawnDistance = 40f;

        [Header("Node Selection — visibility (reconstructed iso camera)")]
        [Tooltip("ON: nodes inside any player's (reconstructed) camera frustum are rejected — " +
                 "spawns always happen off-screen. OFF: distance band only, on-screen spawns " +
                 "allowed — enemies enter via their Spawning state entry animation instead " +
                 "of popping (BaseEnemy.spawnEntryDuration + EnemySpawnEntryPresenter).")]
        [SerializeField] private bool useFrustumCheck = true;

        [Tooltip("World-space offset from the hero to their camera (copy from the Cinemachine rig).")]
        [SerializeField] private Vector3 cameraOffset = new(0f, 12f, -12f);

        [Tooltip("Camera rotation in euler angles (copy from the rig).")]
        [SerializeField] private Vector3 cameraEuler = new(45f, 0f, 0f);

        [SerializeField, Min(1f)] private float cameraFov = 60f;
        [SerializeField, Min(0.1f)] private float cameraAspect = 16f / 9f;
        [SerializeField, Min(1f)] private float cameraFarPlane = 80f;

        [Tooltip("Extra margin (meters) around a node when testing frustum overlap — hides " +
                 "spawn-in even for enemies with large sprites.")]
        [Min(0f)] [SerializeField] private float visibilityPadding = 2f;

        [Header("Housekeeping")]
        [Tooltip("Seconds between alive-list sweeps (catches despawns that skipped OnDied).")]
        [Min(1f)] [SerializeField] private float sweepInterval = 5f;

        // ---- Server-only state ----
        private SectionEncounterSO _encounter;

        // ---- Finale routing (ISpawnDirectorRouting — mimari doküman §4/§5) ----
        private SpawnDirectorMode _routingMode = SpawnDirectorMode.Normal;
        private readonly HashSet<ulong> _targetPool = new(); // EscapeCorridorOnly'de hedef Runner'lar
        private int _activeFloor = -1;                       // -1 = kat filtresi kapalı
        private readonly Dictionary<BaseEnemy, float> _threatByEnemy = new();   // alive -> cost
        private readonly Dictionary<BaseEnemy, int> _aliveByPrefab = new();     // prefab -> count
        private readonly Dictionary<BaseEnemy, BaseEnemy> _prefabByEnemy = new(); // instance -> prefab
        private readonly Dictionary<ulong, float> _nextGuiltSpawnByClient = new();

        private float _usedThreat;
        private float _attention;
        private float _nextSpawnTime;
        private float _nextSpecialTime;
        private float _nextSweepTime;

        // Scratch buffers — zero per-tick allocation.
        private readonly List<EncounterEntry> _entryScratch = new();
        private readonly List<SpawnNode> _nodeScratch = new();
        private readonly Plane[] _planeScratch = new Plane[6];

        /// <summary>Server-side debug/UI surface.</summary>
        public float UsedThreat => _usedThreat;
        public float Attention => _attention;

        /// <summary>
        /// Runtime toggle (server). OFF = distance-band-only node picks: spawns may happen
        /// in full view, covered by the enemies' entry animation. Sections can flip this per
        /// design beat (e.g. Desert Worms erupting on-screen during a Sandstorm).
        /// </summary>
        public bool UseFrustumCheck
        {
            get => useFrustumCheck;
            set => useFrustumCheck = value;
        }

        // ------------------------------------------------------------------ Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[SpawnDirector] Duplicate instance destroyed.", this);
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
            {
                enabled = false; // Clients never run the brain.
                return;
            }

            SectionManager.OnSectionChanged += HandleSectionChanged;
            HandleSectionChanged(SectionManager.CurrentSection);
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) SectionManager.OnSectionChanged -= HandleSectionChanged;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            if (Instance == this) Instance = null;
        }

        private void HandleSectionChanged(int section)
        {
            // Finale'de encounter'ı FinaleManager kat bazlı override eder — section-liste
            // seçimi devre dışı (section zaten 4'te sabit kalır, bu guard savunmacıdır).
            if (_routingMode == SpawnDirectorMode.EscapeCorridorOnly) return;

            _encounter = null;
            foreach (SectionEncounterSO so in encounters)
            {
                if (so != null && so.sectionIndex == section) { _encounter = so; break; }
            }

            // Fresh section, fresh pacing.
            _attention = 0f;
            _nextGuiltSpawnByClient.Clear();
            if (_encounter != null)
                _nextSpawnTime = Time.time + Random.Range(_encounter.spawnInterval.x, _encounter.spawnInterval.y);
        }

        // ------------------------------------------------------------------ Main loop (SERVER)

        private void Update()
        {
            if (!IsServer || _encounter == null || NetworkManager.Singleton == null) return;

            if (Time.time >= _nextSweepTime)
            {
                _nextSweepTime = Time.time + sweepInterval;
                SweepDeadEntries();
            }

            // Attention accrues passively; night makes everything louder.
            float attentionRate = _encounter.passiveAttentionPerSecond;
            if (EnvironmentalEventManager.IsActive(EnvironmentalEventType.NightPhase))
                attentionRate *= _encounter.nightAttentionMultiplier;
            _attention += attentionRate * Time.deltaTime;

            // Special release: attention full + cooldown elapsed.
            if (_attention >= _encounter.specialAttentionThreshold && Time.time >= _nextSpecialTime)
            {
                if (TrySpawnFromPool(_encounter.specialPool))
                {
                    _attention -= _encounter.specialAttentionThreshold;
                    _nextSpecialTime = Time.time + _encounter.specialCooldown;
                }
            }

            // Regular pacing.
            if (Time.time >= _nextSpawnTime)
            {
                TrySpawnFromPool(_encounter.regularPool);
                _nextSpawnTime = Time.time + Random.Range(_encounter.spawnInterval.x, _encounter.spawnInterval.y);
            }

            TickGuiltSpawns();
        }

        // ------------------------------------------------------------------ ISpawnDirectorRouting (SERVER)

        /// <inheritdoc/>
        public void SetMode(SpawnDirectorMode mode)
        {
            if (!IsServer || _routingMode == mode) return;
            _routingMode = mode;

            if (mode == SpawnDirectorMode.Normal)
            {
                // Finale bitti/iptal: filtreleri sıfırla, kayıtlı düşmanları temizle,
                // section-tabanlı encounter seçimine geri dön.
                _targetPool.Clear();
                _activeFloor = -1;
                ServerDespawnAllRegistered();
                HandleSectionChanged(SectionManager.CurrentSection);
            }
        }

        /// <inheritdoc/>
        public void RegisterTargetPool(IReadOnlyList<ulong> clientIds)
        {
            if (!IsServer) return;
            _targetPool.Clear();
            if (clientIds == null) return;
            for (int i = 0; i < clientIds.Count; i++) _targetPool.Add(clientIds[i]);
        }

        /// <inheritdoc/>
        public void SetActiveFloor(int floorIndex)
        {
            if (!IsServer || _activeFloor == floorIndex) return;
            _activeFloor = floorIndex;

            // 5 katlı bina + hızlı tempo: önceki katın setini ayakta tutmak gereksiz yük —
            // kayıtlı tüm düşmanlar despawn edilir, yeni kat kendi setiyle dolar (doküman Not 1).
            ServerDespawnAllRegistered();
        }

        /// <summary>
        /// FinaleManager kat başına encounter atar (section listesi yerine). null = spawn durur.
        /// Section değişimi gelirse liste tabanlı seçim override'ı ezer (Finale'de section sabit 4).
        /// </summary>
        public void ServerSetEncounterOverride(SectionEncounterSO encounter)
        {
            if (!IsServer) return;
            _encounter = encounter;
            _attention = 0f;
            _nextGuiltSpawnByClient.Clear();
            if (_encounter != null)
                _nextSpawnTime = Time.time + Random.Range(_encounter.spawnInterval.x, _encounter.spawnInterval.y);
        }

        /// <summary>EscapeCorridorOnly'de yalnızca target pool'daki client'lar hedef/mesafe referansıdır.</summary>
        private bool IsTargetClient(ulong clientId) =>
            _routingMode == SpawnDirectorMode.Normal || _targetPool.Contains(clientId);

        private bool NodeMatchesActiveFloor(SpawnNode node)
        {
            if (_activeFloor < 0) return true;
            return node.TryGetComponent(out FloorSpawnNodeTag tag) && tag.FloorIndex == _activeFloor;
        }

        private void ServerDespawnAllRegistered()
        {
            if (_threatByEnemy.Count == 0) { _usedThreat = 0f; return; }

            List<BaseEnemy> toRemove = new(_threatByEnemy.Keys);
            foreach (BaseEnemy enemy in toRemove)
            {
                if (enemy == null) continue;
                enemy.OnDied -= HandleEnemyDied;
                if (enemy.NetworkObject != null && enemy.NetworkObject.IsSpawned)
                    enemy.NetworkObject.Despawn(true); // pool handler'ı varsa oraya döner
            }
            _threatByEnemy.Clear();
            _prefabByEnemy.Clear();
            _aliveByPrefab.Clear();
            _usedThreat = 0f;
        }

        // ------------------------------------------------------------------ Public API

        /// <summary>
        /// Feed the Attention meter (server-only, null-safe). Call from ability casts,
        /// loud actions, failed stealth... At night this counts for much more.
        /// </summary>
        public static void ReportAttention(float amount)
        {
            if (Instance == null || !Instance.IsServer || Instance._encounter == null) return;

            if (EnvironmentalEventManager.IsActive(EnvironmentalEventType.NightPhase))
                amount *= Instance._encounter.nightAttentionMultiplier;
            Instance._attention += amount;
        }

        /// <summary>
        /// Server-only. Spawn OUTSIDE the node system (Fission splits, Xenomorph clones)
        /// while still counting against Threat Capacity. Returns null off-server.
        /// </summary>
        public BaseEnemy ServerSpawnAt(BaseEnemy prefab, Vector3 position, float threatCost)
        {
            if (!IsServer || prefab == null) return null;

            BaseEnemy enemy = NetworkEnemyPool.Instance != null
                ? NetworkEnemyPool.Instance.ServerSpawn(prefab, position, Quaternion.identity)
                : null;

            if (enemy != null) RegisterSpawn(enemy, prefab, threatCost);
            return enemy;
        }

        // ------------------------------------------------------------------ Spawning core

        private bool TrySpawnFromPool(List<EncounterEntry> pool)
        {
            if (pool == null || pool.Count == 0) return false;
            if (_threatByEnemy.Count >= _encounter.maxAliveEnemies) return false;

            float budget = _encounter.threatCapacity - _usedThreat;
            EnvironmentalEventType events = EnvironmentalEventManager.ActiveEvents;

            // Affordable candidates with event-adjusted weights.
            _entryScratch.Clear();
            foreach (EncounterEntry entry in pool)
            {
                if (entry.prefab == null || entry.threatCost > budget) continue;
                if (entry.maxAlive > 0 && GetAliveCount(entry.prefab) >= entry.maxAlive) continue;
                if (_encounter.GetEffectiveWeight(entry, events) <= 0f) continue;
                _entryScratch.Add(entry);
            }

            // Weighted pick, then find a node; if the pick has no usable node, drop it and retry.
            while (_entryScratch.Count > 0)
            {
                EncounterEntry chosen = WeightedPick(_entryScratch, events);
                SpawnNode node = PickNode(chosen.allowedNodeTypes);

                if (node == null)
                {
                    _entryScratch.Remove(chosen);
                    continue;
                }

                BaseEnemy enemy = node.ServerSpawnEnemy(chosen.prefab);
                if (enemy == null) return false;

                RegisterSpawn(enemy, chosen.prefab, chosen.threatCost);
                return true;
            }
            return false;
        }

        private EncounterEntry WeightedPick(List<EncounterEntry> entries, EnvironmentalEventType events)
        {
            float total = 0f;
            foreach (EncounterEntry e in entries) total += _encounter.GetEffectiveWeight(e, events);

            float roll = Random.value * total;
            foreach (EncounterEntry e in entries)
            {
                roll -= _encounter.GetEffectiveWeight(e, events);
                if (roll <= 0f) return e;
            }
            return entries[^1];
        }

        private void RegisterSpawn(BaseEnemy enemy, BaseEnemy prefab, float threatCost)
        {
            _threatByEnemy[enemy] = threatCost;
            _prefabByEnemy[enemy] = prefab;
            _aliveByPrefab[prefab] = GetAliveCount(prefab) + 1;
            _usedThreat += threatCost;

            enemy.OnDied += HandleEnemyDied;
        }

        private void HandleEnemyDied(BaseEnemy enemy)
        {
            enemy.OnDied -= HandleEnemyDied; // Pooled instances get re-subscribed next life.
            UnregisterEnemy(enemy);
        }

        private void UnregisterEnemy(BaseEnemy enemy)
        {
            if (_threatByEnemy.TryGetValue(enemy, out float cost))
            {
                _usedThreat = Mathf.Max(0f, _usedThreat - cost);
                _threatByEnemy.Remove(enemy);
            }
            if (_prefabByEnemy.TryGetValue(enemy, out BaseEnemy prefab))
            {
                _aliveByPrefab[prefab] = Mathf.Max(0, GetAliveCount(prefab) - 1);
                _prefabByEnemy.Remove(enemy);
            }
        }

        private int GetAliveCount(BaseEnemy prefab) =>
            _aliveByPrefab.TryGetValue(prefab, out int n) ? n : 0;

        private void SweepDeadEntries()
        {
            List<BaseEnemy> stale = null;
            foreach (KeyValuePair<BaseEnemy, float> kvp in _threatByEnemy)
            {
                BaseEnemy e = kvp.Key;
                if (e == null || !e.isActiveAndEnabled || !e.IsAlive)
                    (stale ??= new List<BaseEnemy>()).Add(e);
            }
            if (stale == null) return;
            foreach (BaseEnemy e in stale)
            {
                if (e != null) e.OnDied -= HandleEnemyDied;
                UnregisterEnemy(e);
            }
        }

        // ------------------------------------------------------------------ Node selection

        /// <summary>
        /// A node matching the type mask, off cooldown, inside the pressure band and hidden
        /// from every player. Random among valid so one hole doesn't monopolize.
        /// </summary>
        private SpawnNode PickNode(SpawnNodeType allowedTypes)
        {
            _nodeScratch.Clear();

            foreach (SpawnNode node in SpawnNode.ActiveNodes)
            {
                if ((node.NodeType & allowedTypes) == 0 || !node.IsAvailable) continue;
                if (!NodeMatchesActiveFloor(node)) continue; // Finale: sadece aktif katın node'ları
                if (!PassesDistanceBand(node.SpawnPosition)) continue;
                if (useFrustumCheck && IsVisibleToAnyPlayer(node.SpawnPosition)) continue;
                _nodeScratch.Add(node);
            }

            return _nodeScratch.Count > 0 ? _nodeScratch[Random.Range(0, _nodeScratch.Count)] : null;
        }

        /// <summary>Nearest valid node to a world position — Mark of Guilt targeting.</summary>
        private SpawnNode PickNodeNearest(SpawnNodeType allowedTypes, Vector3 target)
        {
            SpawnNode best = null;
            float bestSqr = float.MaxValue;

            foreach (SpawnNode node in SpawnNode.ActiveNodes)
            {
                if ((node.NodeType & allowedTypes) == 0 || !node.IsAvailable) continue;
                if (!NodeMatchesActiveFloor(node)) continue; // Finale: sadece aktif katın node'ları
                if (!PassesDistanceBand(node.SpawnPosition)) continue;
                if (useFrustumCheck && IsVisibleToAnyPlayer(node.SpawnPosition)) continue;

                float sqr = (node.SpawnPosition - target).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = node; }
            }
            return best;
        }

        private bool PassesDistanceBand(Vector3 pos)
        {
            bool inRangeOfSomeone = false;

            foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null || !IsTargetClient(client.ClientId)) continue;
                float dist = Vector3.Distance(pos, client.PlayerObject.transform.position);

                if (dist < minSpawnDistance) return false;       // Too close to ANY player.
                if (dist <= maxSpawnDistance) inRangeOfSomeone = true;
            }
            return inRangeOfSomeone;
        }

        /// <summary>
        /// Rebuilds each player's frustum from the FIXED iso rig (offset/rotation/FOV are
        /// identical on every machine, so position is all the server needs) and tests the
        /// node's padded bounds against it. True = someone would see the spawn.
        /// </summary>
        private bool IsVisibleToAnyPlayer(Vector3 pos)
        {
            var bounds = new Bounds(pos, Vector3.one * (visibilityPadding * 2f));
            Quaternion camRot = Quaternion.Euler(cameraEuler);
            Matrix4x4 proj = Matrix4x4.Perspective(cameraFov, cameraAspect, 0.1f, cameraFarPlane);

            foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null || !IsTargetClient(client.ClientId)) continue;

                Vector3 camPos = client.PlayerObject.transform.position + cameraOffset;

                // Unity view space looks down -Z: flip Z of the inverse camera TRS.
                Matrix4x4 worldToCam = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) *
                                       Matrix4x4.TRS(camPos, camRot, Vector3.one).inverse;

                GeometryUtility.CalculateFrustumPlanes(proj * worldToCam, _planeScratch);
                if (GeometryUtility.TestPlanesAABB(_planeScratch, bounds)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ Mark of Guilt (House)

        private void TickGuiltSpawns()
        {
            if (_encounter.guiltMarkEffect == null ||
                _encounter.guiltSpawnEntry == null ||
                _encounter.guiltSpawnEntry.prefab == null) return;

            foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null || !IsTargetClient(client.ClientId)) continue;
                if (!client.PlayerObject.TryGetComponent(out StatusEffectController status)) continue;
                if (!status.HasEffect(_encounter.guiltMarkEffect)) continue;

                _nextGuiltSpawnByClient.TryGetValue(client.ClientId, out float nextTime);
                if (Time.time < nextTime) continue;

                // The Echo comes from the node CLOSEST to the guilty player.
                Vector3 playerPos = client.PlayerObject.transform.position;
                SpawnNode node = PickNodeNearest(_encounter.guiltSpawnEntry.allowedNodeTypes, playerPos);
                if (node == null) continue;

                BaseEnemy echo = node.ServerSpawnEnemy(_encounter.guiltSpawnEntry.prefab);
                if (echo == null) continue;

                RegisterSpawn(echo, _encounter.guiltSpawnEntry.prefab, _encounter.guiltSpawnEntry.threatCost);
                _nextGuiltSpawnByClient[client.ClientId] = Time.time + _encounter.guiltSpawnInterval;
            }
        }
    }
}
