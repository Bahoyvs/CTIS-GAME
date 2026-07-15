using System;
using System.Collections.Generic;
using CBuilding.Heroes;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Core
{
    /// <summary>
    /// GS-2 / GameFlow — networked "which Section is the run in" authority PLUS the
    /// Section 1→2→3 transition loop (revival + teleport). One instance per gameplay
    /// scene (next to NetworkGameManager). Server writes, everyone reads.
    ///
    /// Consumers today:
    ///   - ComposedBasicAttackBehaviour (GS-17 §7.1) swaps its active basic-attack SO.
    ///   - APBasicAttackController (GS-17 §6.4) gates chain-shot target counts.
    ///   - SpawnDirector picks its SectionEncounterSO by section index.
    ///   - EnvironmentManager enables the biome environment for the new section.
    /// All subscribe to <see cref="OnSectionChanged"/> — a plain static event so prefab
    /// components don't need a scene reference to this object.
    ///
    /// TRANSITION FLOW (GDD roguelite death rules): the section boss dies →
    /// ServerCompleteCurrentSection() → revive all dead heroes at FULL HP → teleport
    /// EVERYONE to the next section's spawn points → increment CurrentSection (which
    /// fires OnSectionChanged, driving SpawnDirector + EnvironmentManager for free).
    /// After Section 3's boss, OnAllSectionsCompleted fires instead (Finale handoff —
    /// out of scope here).
    /// </summary>
    public class SectionManager : NetworkBehaviour
    {
        public static SectionManager Instance { get; private set; }

        /// <summary>Section 4 = Final Phase. FinaleManager (CBuilding.Finale) bu değere geçildiğinde devralır.</summary>
        public const int FinaleSection = 4;

        /// <summary>Last section handled by THIS manager's loop (1..3). Beyond it: Finale handoff.</summary>
        public const int LastStandardSection = 3;

        [Serializable]
        private class SectionSpawnSet
        {
            [Tooltip("Which section these points belong to (players are teleported HERE when the section STARTS).")]
            [Range(1, LastStandardSection)] public int section = 1;
            public List<Transform> spawnPoints = new();
        }

        [Header("Per-section start points")]
        [Tooltip("Where players appear at the START of each section. Section 1's set is optional " +
                 "(PlayerSpawner already owns initial spawning).")]
        [SerializeField] private List<SectionSpawnSet> sectionSpawns = new();

        /// <summary>Fired on EVERY peer whenever the section changes. Payload: 1..4 (4 = Finale).</summary>
        public static event Action<int> OnSectionChanged;

        /// <summary>
        /// SERVER-ONLY: Section 3's boss died — the standard run is complete. The Finale
        /// glue (or a results flow) subscribes to this; SectionManager itself deliberately
        /// knows nothing about Section 4 internals (no CBuilding.Finale dependency).
        /// </summary>
        public static event Action OnAllSectionsCompleted;

        private readonly NetworkVariable<int> _netSection = new(
            1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Current section (1-based). Valid on every peer.</summary>
        public static int CurrentSection => Instance != null ? Instance._netSection.Value : 1;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[SectionManager] Duplicate instance destroyed.", this);
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            _netSection.OnValueChanged += HandleSectionChanged;
            // Late joiners: replay the current value so subscribers initialize correctly.
            OnSectionChanged?.Invoke(_netSection.Value);
        }

        public override void OnNetworkDespawn()
        {
            _netSection.OnValueChanged -= HandleSectionChanged;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            if (Instance == this) Instance = null;
        }

        private void HandleSectionChanged(int previous, int current)
        {
            OnSectionChanged?.Invoke(current);
        }

        // ---- Server API ----

        /// <summary>Server-only. Clamped to 1..4 (4 = Final Phase handoff).</summary>
        public void ServerSetSection(int section)
        {
            if (!IsServer) return;
            _netSection.Value = Mathf.Clamp(section, 1, FinaleSection);
        }

        /// <summary>Server-only convenience for GameFlow transitions.</summary>
        public void ServerAdvanceSection() => ServerSetSection(_netSection.Value + 1);

        /// <summary>
        /// SERVER-ONLY. Call when the CURRENT section's boss dies. Runs the full transition:
        ///   1. revive every dead hero at full HP (spectate exit via BaseHero.OnRevived),
        ///   2. teleport everyone to the NEXT section's spawn points,
        ///   3. increment CurrentSection (fires OnSectionChanged for all subscribers).
        /// After Section 3 it fires OnAllSectionsCompleted instead of incrementing.
        /// </summary>
        public void ServerCompleteCurrentSection()
        {
            if (!IsServer) return;

            int current = _netSection.Value;
            if (current >= LastStandardSection)
            {
                OnAllSectionsCompleted?.Invoke();
                return;
            }

            int next = current + 1;
            ReviveAndTeleportAll(next);
            _netSection.Value = next; // fires OnSectionChanged → EnvironmentManager, SpawnDirector, hero kits
        }

        // ---- Transition internals (SERVER) ----

        private void ReviveAndTeleportAll(int nextSection)
        {
            List<Transform> points = GetSpawnPoints(nextSection);
            int slot = 0;

            foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
            {
                if (client.PlayerObject == null ||
                    !client.PlayerObject.TryGetComponent(out BaseHero hero)) continue;

                // GDD rule: if the team killed the boss, the dead come back at FULL HP.
                if (!hero.IsAlive) hero.ServerReviveFullHealth();

                Vector3 pos = points.Count > 0
                    ? points[slot++ % points.Count].position
                    : transform.position;

                // Movement is owner-authoritative (ClientNetworkTransform): the teleport must
                // happen on the owning client, then replicates from there — same pattern as
                // FinaleManager.
                TeleportLocalHeroRpc(pos, RpcTarget.Single(client.ClientId, RpcTargetUse.Temp));
            }
        }

        private List<Transform> GetSpawnPoints(int section)
        {
            foreach (SectionSpawnSet set in sectionSpawns)
                if (set.section == section) return set.spawnPoints;
            return new List<Transform>();
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void TeleportLocalHeroRpc(Vector3 position, RpcParams rpcParams = default)
        {
            NetworkObject player = NetworkManager.LocalClient?.PlayerObject;
            if (player != null) player.transform.position = position;
        }
    }
}
