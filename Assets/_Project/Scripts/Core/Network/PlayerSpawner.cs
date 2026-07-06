using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Network
{
    /// <summary>
    /// Server-side player spawning. Runs ONLY on the server: when a client connects
    /// (including the host's own local client), instantiate their hero and hand them
    /// ownership via SpawnAsPlayerObject — that is what makes IsOwner true on exactly
    /// one machine per hero.
    ///
    /// SETUP: scene object in the gameplay scene. Assign the hero prefab (must be a
    /// registered Network Prefab with NetworkObject + ClientNetworkTransform) and 4
    /// spawn point transforms. NetworkManager's built-in "Player Prefab" slot stays
    /// EMPTY so NGO doesn't double-spawn.
    /// </summary>
    public class PlayerSpawner : MonoBehaviour
    {
        [Header("Prefabs")]
        [Tooltip("MVP: every player gets the same hero. Later: per-player hero selection " +
                 "sent during connection approval payload.")]
        [SerializeField] private NetworkObject heroPrefab;

        [Header("Spawn Points")]
        [SerializeField] private List<Transform> spawnPoints = new();

        private int _nextSpawnIndex;

        private void Start()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                Debug.LogError("[PlayerSpawner] No NetworkManager in scene.", this);
                return;
            }

            nm.OnClientConnectedCallback += HandleClientConnected;

            // Edge case: server already running before this scene object woke up
            // (host started in menu scene, then scene-switched here). Spawn for
            // everyone already connected.
            if (nm.IsServer)
                foreach (NetworkClient client in nm.ConnectedClientsList)
                    SpawnHeroFor(client.ClientId);
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
        }

        private void HandleClientConnected(ulong clientId)
        {
            // This callback fires on every peer; only the server may spawn.
            if (!NetworkManager.Singleton.IsServer) return;
            SpawnHeroFor(clientId);
        }

        private void SpawnHeroFor(ulong clientId)
        {
            // Idempotency guard — client might already have a player object (reconnect,
            // or the ConnectedClientsList loop above racing the callback).
            NetworkClient client = NetworkManager.Singleton.ConnectedClients[clientId];
            if (client.PlayerObject != null) return;

            Transform point = spawnPoints.Count > 0
                ? spawnPoints[_nextSpawnIndex++ % spawnPoints.Count]
                : transform;

            // Plain Instantiate is local-only; Spawn*() is what replicates the object.
            NetworkObject hero = Instantiate(heroPrefab, point.position, Quaternion.identity);
            // destroyWithScene:true — heroes die with the gameplay scene on scene switch.
            hero.SpawnAsPlayerObject(clientId, destroyWithScene: true);

            Debug.Log($"[Server] Spawned hero for client {clientId} at {point.position}.");
        }
    }
}
