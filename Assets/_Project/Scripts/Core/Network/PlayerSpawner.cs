using CBuilding.Data;
using CBuilding.Lobby;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Network
{
    /// <summary>
    /// Server-side player spawning updated for Lobby Architecture.
    /// Runs ONLY on the server: when a client connects (or when scene switches from Lobby),
    /// reads their selected hero ID from LobbyNetworkManager, pulls the correct prefab 
    /// from HeroCatalogSO, and assigns ownership.
    /// </summary>
    public class PlayerSpawner : MonoBehaviour
    {
        [Header("Data")]
        [Tooltip("Lobi seçimlerini çözmek için kullanılan kahraman veri kataloğu.")]
        [SerializeField] private HeroCatalogSO heroCatalog;

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

            if (heroCatalog == null)
            {
                Debug.LogError("[PlayerSpawner] Hero Catalog is missing! Assign it in the Inspector.", this);
                return;
            }

            nm.OnClientConnectedCallback += HandleClientConnected;

            // Edge case: server already running before this scene object woke up
            // (host started in menu scene, then scene-switched here). Spawn for
            // everyone already connected.
            if (nm.IsServer)
            {
                foreach (NetworkClient client in nm.ConnectedClientsList)
                {
                    SpawnHeroFor(client.ClientId);
                }
            }
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

            // 1. Lobide kaydedilen snapshot verilerinden bu oyuncunun seçtiği Hero ID'sini çek.
            // Eğer doğrudan sahneyi test ediyorsak ve lobi kaydı yoksa fallback olarak ilk kahramanı (0) seç.
            int heroId = LobbyNetworkManager.HeroSelections.TryGetValue(clientId, out int id) ? id : 0;

            // 2. ID'yi kullanarak katalogdan doğru karakter verisini bul ve üzerindeki asıl oynanabilir "GameplayPrefab"ı al.
            var heroData = heroCatalog.GetHero(heroId);
            if (heroData == null || heroData.GameplayPrefab == null)
            {
                Debug.LogError($"[PlayerSpawner] Cannot spawn hero! Hero Data or Gameplay Prefab is null for Hero ID: {heroId}");
                return;
            }

            NetworkObject heroPrefab = heroData.GameplayPrefab.GetComponent<NetworkObject>();
            if (heroPrefab == null)
            {
                Debug.LogError($"[PlayerSpawner] The Gameplay Prefab assigned to Hero ID {heroId} does not have a NetworkObject component!");
                return;
            }

            // 3. Dinamik olarak seçilen doğru prefab'ı doğur (Instantiate)
            NetworkObject hero = Instantiate(heroPrefab, point.position, Quaternion.identity);

            // destroyWithScene:true — heroes die with the gameplay scene on scene switch.
            hero.SpawnAsPlayerObject(clientId, destroyWithScene: true);

            Debug.Log($"[Server] Spawned dynamic hero [{heroData.name}] for client {clientId} at {point.position}.");
        }
    }
}