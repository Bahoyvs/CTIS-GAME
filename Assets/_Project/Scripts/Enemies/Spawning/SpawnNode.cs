using System.Collections.Generic;
using CBuilding.StatusEffects;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace CBuilding.Enemies.Spawning
{
    /// <summary>
    /// Physical spawn point in the scene: wall hole, ceiling grate, air vent, sand mound...
    /// The SpawnDirector picks a node and tells it WHAT to produce; the node owns WHERE and
    /// applies its hack state (Bahadır's Ultimate) to everything it births.
    ///
    /// PREFAB: SpawnNode + NetworkObject + a Collider (isTrigger recommended) so hero
    /// abilities can target it via IHackable. Register in Network Prefabs if instantiated
    /// at runtime; scene-placed nodes just need the NetworkObject.
    ///
    /// AUTHORITY: all spawning/hacking runs on the server. IsHacked replicates via
    /// NetworkVariable purely so clients can show "corrupted node" VFX.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SpawnNode : NetworkBehaviour, IHackable
    {
        /// <summary>Server-side registry the Director iterates. No scene scans per tick.</summary>
        public static readonly List<SpawnNode> ActiveNodes = new();

        [Header("Node")]
        [Tooltip("Physical archetype — filters which enemies may use this node.")]
        [SerializeField] private SpawnNodeType nodeType = SpawnNodeType.Ground;

        [Tooltip("Where enemies actually appear. Null = this transform.")]
        [SerializeField] private Transform spawnPoint;

        [Tooltip("Seconds this node rests after producing an enemy (prevents conga lines " +
                 "out of a single hole).")]
        [Min(0f)] [SerializeField] private float spawnCooldown = 3f;

        [Tooltip("Max NavMesh sampling distance around the spawn point.")]
        [Min(0.1f)] [SerializeField] private float navMeshSampleRadius = 2f;

        [Header("Hacking")]
        [Tooltip("Can Bahadır's Ultimate corrupt this node at all?")]
        [SerializeField] private bool hackable = true;

        [Header("Presentation Hooks")]
        [Tooltip("Enabled while the node is hacked (glitch VFX, green glow...). Optional.")]
        [SerializeField] private GameObject hackedVisual;

        // Replicated purely for client VFX; gameplay reads the server-side timer.
        private readonly NetworkVariable<bool> _netIsHacked = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // ---- Server-only hack state ----
        private float _hackEndTime;
        private EffectDataSO _hackEffect;
        private GameObject _hacker;
        private float _nextSpawnAllowedTime;

        public SpawnNodeType NodeType => nodeType;
        public Vector3 SpawnPosition => spawnPoint != null ? spawnPoint.position : transform.position;

        /// <summary>Valid on server (authoritative) and clients (replicated, VFX-grade).</summary>
        public bool IsHacked => IsServer ? Time.time < _hackEndTime : _netIsHacked.Value;

        /// <summary>Server-side: ready to produce (off cooldown)?</summary>
        public bool IsAvailable => Time.time >= _nextSpawnAllowedTime;

        // ------------------------------------------------------------------ Lifecycle

        public override void OnNetworkSpawn()
        {
            if (IsServer) ActiveNodes.Add(this);
            _netIsHacked.OnValueChanged += HandleHackedChanged;
            ApplyHackedVisual(_netIsHacked.Value);
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) ActiveNodes.Remove(this);
            _netIsHacked.OnValueChanged -= HandleHackedChanged;
        }

        private void Update()
        {
            // Server ticks hack expiry so the replicated flag (and VFX) turn off on time.
            if (!IsServer || !_netIsHacked.Value) return;
            if (Time.time >= _hackEndTime)
            {
                _netIsHacked.Value = false;
                _hackEffect = null;
                _hacker = null;
            }
        }

        private void HandleHackedChanged(bool previous, bool current) => ApplyHackedVisual(current);

        private void ApplyHackedVisual(bool hacked)
        {
            if (hackedVisual != null) hackedVisual.SetActive(hacked);
        }

        // ------------------------------------------------------------------ IHackable

        public bool CanBeHacked => hackable && !IsHacked;

        public void ServerHack(GameObject hacker, EffectDataSO virusEffect, float duration)
        {
            if (!IsServer || !hackable || virusEffect == null || duration <= 0f) return;

            _hacker = hacker;
            _hackEffect = virusEffect;
            _hackEndTime = Time.time + duration;
            _netIsHacked.Value = true;
        }

        // ------------------------------------------------------------------ Spawning (SERVER)

        /// <summary>
        /// Server-only. Produces one enemy of the given prefab through the network pool,
        /// snapped to the NavMesh at the spawn point. If the node is hacked, the virus
        /// effect is applied the same frame the enemy spawns — before it can act.
        /// Returns null on failure (no NavMesh nearby / not server).
        /// </summary>
        public BaseEnemy ServerSpawnEnemy(BaseEnemy prefab)
        {
            if (!IsServer || prefab == null) return null;

            Vector3 pos = SpawnPosition;
            // Ceiling/wall nodes sit off the NavMesh — project onto it so the agent is valid.
            if (NavMesh.SamplePosition(pos, out NavMeshHit hit, navMeshSampleRadius, NavMesh.AllAreas))
                pos = hit.position;

            BaseEnemy enemy = NetworkEnemyPool.Instance != null
                ? NetworkEnemyPool.Instance.ServerSpawn(prefab, pos, transform.rotation)
                : FallbackInstantiate(prefab, pos);

            if (enemy == null) return null;

            _nextSpawnAllowedTime = Time.time + spawnCooldown;

            // Virus injection: applied the instant the enemy exists on the network, so a
            // hacked node's spawns are born carrying Bahadır's Spyware.
            if (IsHacked && _hackEffect != null &&
                enemy.TryGetComponent(out StatusEffectController status))
            {
                status.ApplyEffect(_hackEffect, _hacker);
            }

            return enemy;
        }

        private static BaseEnemy FallbackInstantiate(BaseEnemy prefab, Vector3 pos)
        {
            // Pool missing (early testing / Step 2 of the roadmap): plain Instantiate+Spawn.
            BaseEnemy enemy = Instantiate(prefab, pos, Quaternion.identity);
            enemy.NetworkObject.Spawn(true);
            return enemy;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = nodeType switch
            {
                SpawnNodeType.Ceiling => Color.cyan,
                SpawnNodeType.Sand    => Color.yellow,
                SpawnNodeType.Vent    => Color.green,
                SpawnNodeType.Wall    => new Color(1f, 0.5f, 0f),
                SpawnNodeType.Void    => Color.magenta,
                _                     => Color.white,
            };
            Gizmos.DrawWireCube(SpawnPosition, Vector3.one * 0.6f);
            Gizmos.DrawLine(transform.position, SpawnPosition);
        }
#endif
    }
}
