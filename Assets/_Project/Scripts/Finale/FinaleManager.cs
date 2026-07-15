using System;
using System.Collections;
using System.Collections.Generic;
using CBuilding.Core;
using CBuilding.Enemies.Spawning;
using CBuilding.Heroes;
using CBuilding.Network;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Finale
{
    /// <summary>
    /// Section 4 (Final Phase) faz state machine — mimari doküman §3 (KOMPLE YENİ sistem;
    /// eski Overwatch/God's Eye/Hack-Tuzak-Marking/Upload-Bar/Scenario A-B tasarımının
    /// yerini alır).
    ///
    ///   Voting  → VotingManager (tie-break: en yüksek HP, sonra rastgele)
    ///   JackIn  → Defender bedeni Core noktasına, Runner'lar (N-1) Bodrum'a
    ///   Escape  → kat kat tırmanış (FloorConvergenceTracker) + EscapeTimerController;
    ///             SpawnDirector EscapeCorridorOnly + kat bazlı hedef havuzu
    ///   Resolved→ Win (çatıya süre içinde) / Lose (timer doldu → patlama, herkes ölür).
    ///             Loot-tier ayrımı YOK — bölümü bitirmek doğrudan zaferdir.
    ///
    /// Section 4 içinde dirilme YOK: HP 0 = koşunun kalanında spectate (Section 1-3'ün
    /// "bölüm sonunda dirilme" kuralı burada geçerli değil).
    ///
    /// SETUP: Finale sahnesinde NetworkObject'li GameObject; Voting/Timer/Tracker referansları
    /// ve spawn noktaları Inspector'dan atanır. SectionManager.ServerAdvanceSection() 4'e
    /// geldiğinde ServerBeginFinale() çağrılır (GameFlow handoff).
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class FinaleManager : NetworkBehaviour
    {
        public static FinaleManager Instance { get; private set; }

        [Header("Sub-systems (Inspector'dan ata)")]
        [SerializeField] private VotingManager votingManager;
        [SerializeField] private EscapeTimerController escapeTimer;
        [SerializeField] private FloorConvergenceTracker convergenceTracker;

        [Header("Spawn Points")]
        [Tooltip("Runner'ların Bodrum (kat 0) başlangıç noktaları.")]
        [SerializeField] private List<Transform> runnerSpawnPoints = new();

        [Tooltip("Defender bedeninin bırakıldığı Portal Core yanı nokta.")]
        [SerializeField] private Transform coreBodyPoint;

        [Header("Pacing")]
        [Tooltip("JackIn sunum fazının süresi (sn) — dönüşüm VFX/kamera geçişi için.")]
        [SerializeField, Min(0f)] private float jackInDuration = 3f;

        [Header("Per-floor encounters (index = kat; 0 = Bodrum)")]
        [Tooltip("Her katın kendi düşman/hazard seti (doküman §3.3). Boş bırakılan kat spawnsız kalır.")]
        [SerializeField] private List<SectionEncounterSO> floorEncounters = new();

        // ---- Replicated state (server-write / everyone-read) ----

        private readonly NetworkVariable<FinalePhase> _netPhase = new(
            FinalePhase.Inactive, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ulong> _netDefenderClientId = new(
            ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _netCurrentFloor = new(
            FinaleFloors.Basement, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _netVictory = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public FinalePhase CurrentPhase => _netPhase.Value;
        public ulong DefenderClientId => _netDefenderClientId.Value;
        public int CurrentFloor => _netCurrentFloor.Value;
        public bool Victory => _netVictory.Value;

        /// <summary>Her peer'de: faz değişti (HUD/ResultsManager hook).</summary>
        public event Action<FinalePhase> OnPhaseChanged;

        /// <summary>Her peer'de: takım yeni kata geçti (kapı/asansör LD hook'ları).</summary>
        public event Action<int> OnFloorAdvanced;

        // ---- Server-only state ----
        private readonly List<ulong> _runnerIds = new();       // Escape başındaki tüm Runner'lar
        private readonly HashSet<ulong> _aliveRunnerIds = new();
        private readonly List<BaseHero> _subscribedHeroes = new();
        private BaseHero _defenderHero;

        // ------------------------------------------------------------------ Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            _netPhase.OnValueChanged += HandlePhaseChanged;
            _netCurrentFloor.OnValueChanged += HandleFloorChanged;

            if (IsServer)
            {
                if (votingManager != null) votingManager.OnDefenderChosen += ServerHandleDefenderChosen;
                if (escapeTimer != null) escapeTimer.OnExpired += ServerHandleTimerExpired;
                if (convergenceTracker != null) convergenceTracker.OnFloorConverged += ServerHandleFloorConverged;
                NetworkManager.OnClientDisconnectCallback += ServerHandleClientDisconnected;
            }
        }

        public override void OnNetworkDespawn()
        {
            _netPhase.OnValueChanged -= HandlePhaseChanged;
            _netCurrentFloor.OnValueChanged -= HandleFloorChanged;

            if (IsServer)
            {
                if (votingManager != null) votingManager.OnDefenderChosen -= ServerHandleDefenderChosen;
                if (escapeTimer != null) escapeTimer.OnExpired -= ServerHandleTimerExpired;
                if (convergenceTracker != null) convergenceTracker.OnFloorConverged -= ServerHandleFloorConverged;
                if (NetworkManager != null)
                    NetworkManager.OnClientDisconnectCallback -= ServerHandleClientDisconnected;
                UnsubscribeHeroDeaths();
            }
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        private void HandlePhaseChanged(FinalePhase previous, FinalePhase current) =>
            OnPhaseChanged?.Invoke(current);

        private void HandleFloorChanged(int previous, int current) =>
            OnFloorAdvanced?.Invoke(current);

        // ------------------------------------------------------------------ Entry (SERVER)

        /// <summary>Section 3 boss'u düştükten sonra GameFlow çağırır. Idempotent.</summary>
        public void ServerBeginFinale()
        {
            if (!IsServer || _netPhase.Value != FinalePhase.Inactive) return;

            SectionManager.Instance?.ServerSetSection(SectionManager.FinaleSection);
            _netPhase.Value = FinalePhase.Voting;
            votingManager.ServerBeginVote();
        }

        // ------------------------------------------------------------------ Voting → JackIn

        private void ServerHandleDefenderChosen(ulong defenderClientId)
        {
            if (_netPhase.Value != FinalePhase.Voting) return;

            _netDefenderClientId.Value = defenderClientId;
            _defenderHero = GetHeroOf(defenderClientId);
            _netPhase.Value = FinalePhase.JackIn;

            // Roster: Defender dışındaki herkes Runner (N-1 formülü — 4 hardcode edilmez,
            // 2-4 oyuncuyla da çalışır; min oyuncu Kerem'den teyit bekliyor).
            _runnerIds.Clear();
            _aliveRunnerIds.Clear();
            foreach (ulong id in NetworkManager.ConnectedClientsIds)
            {
                if (id == defenderClientId) continue;
                _runnerIds.Add(id);
                BaseHero hero = GetHeroOf(id);
                if (hero != null && hero.IsAlive) _aliveRunnerIds.Add(id);
            }

            // Beden Core yanına, Runner'lar Bodrum'a. (Hareket owner-authoritative olduğu
            // için teleport, hedef client'ta yapılır — ClientNetworkTransform replike eder.)
            if (coreBodyPoint != null)
                TeleportLocalHeroRpc(coreBodyPoint.position, RpcTarget.Single(defenderClientId, RpcTargetUse.Temp));

            for (int i = 0; i < _runnerIds.Count; i++)
            {
                Vector3 pos = runnerSpawnPoints.Count > 0
                    ? runnerSpawnPoints[i % runnerSpawnPoints.Count].position
                    : transform.position;
                TeleportLocalHeroRpc(pos, RpcTarget.Single(_runnerIds[i], RpcTargetUse.Temp));
            }

            SubscribeHeroDeaths();
            StartCoroutine(JackInRoutine());
        }

        private IEnumerator JackInRoutine()
        {
            yield return new WaitForSeconds(jackInDuration);
            ServerEnterEscape();
        }

        // ------------------------------------------------------------------ Escape

        private void ServerEnterEscape()
        {
            if (_netPhase.Value != FinalePhase.JackIn) return;
            _netPhase.Value = FinalePhase.Escape;
            _netCurrentFloor.Value = FinaleFloors.Basement;

            // Doküman Entegrasyon Notu 1: mod + hedef havuzu + kat, Section 1-3'teki gibi.
            if (SpawnDirector.Instance is ISpawnDirectorRouting routing)
            {
                routing.SetMode(SpawnDirectorMode.EscapeCorridorOnly);
                routing.RegisterTargetPool(new List<ulong>(_aliveRunnerIds));
                routing.SetActiveFloor(FinaleFloors.Basement);
            }
            ApplyFloorEncounter(FinaleFloors.Basement);

            convergenceTracker.ServerConfigure(() => _aliveRunnerIds);
            convergenceTracker.ServerWatchFloor(FinaleFloors.Basement);

            SpiritAbilityController.Instance?.ServerResetForEscape(); // Ruh Enerjisi full başlar
            escapeTimer.ServerStart(); // Escape Timer bu fazın başında başlar (§3.2).
        }

        private void ServerHandleFloorConverged(int floor)
        {
            if (_netPhase.Value != FinalePhase.Escape || floor != _netCurrentFloor.Value) return;

            if (floor == FinaleFloors.Roof)
            {
                // Süre dolmadan hayattaki tüm Runner'lar çatıda → zafer (§3.6).
                ServerResolve(victory: true);
                return;
            }

            int next = floor + 1;
            _netCurrentFloor.Value = next;

            if (SpawnDirector.Instance is ISpawnDirectorRouting routing)
                routing.SetActiveFloor(next); // önceki katın düşmanları despawn (Not 1)
            ApplyFloorEncounter(next);

            convergenceTracker.ServerWatchFloor(next);
        }

        private void ApplyFloorEncounter(int floor)
        {
            SectionEncounterSO encounter =
                floor >= 0 && floor < floorEncounters.Count ? floorEncounters[floor] : null;
            SpawnDirector.Instance?.ServerSetEncounterOverride(encounter);
        }

        // ------------------------------------------------------------------ Deaths & disconnects

        private void SubscribeHeroDeaths()
        {
            UnsubscribeHeroDeaths();
            foreach (ulong id in NetworkManager.ConnectedClientsIds)
            {
                BaseHero hero = GetHeroOf(id);
                if (hero == null) continue;
                hero.OnDied += ServerHandleHeroDied;
                _subscribedHeroes.Add(hero);
            }
        }

        private void UnsubscribeHeroDeaths()
        {
            foreach (BaseHero hero in _subscribedHeroes)
                if (hero != null) hero.OnDied -= ServerHandleHeroDied;
            _subscribedHeroes.Clear();
        }

        private void ServerHandleHeroDied(BaseHero hero)
        {
            if (!IsServer || _netPhase.Value is not (FinalePhase.JackIn or FinalePhase.Escape)) return;

            // Spirit'in HP'si YOK (Systems Design 2026-07-15, madde 1): Defender bedeni
            // oyun akışını etkilemez — Spirit run boyunca aktif kalır. Kaynağı Ruh Enerjisi
            // barıdır (SpiritAbilityController), can barı değil.
            if (hero == _defenderHero) return;

            // Section 4 ölüm kuralı (§3.3): down-state yok, dirilme yok — spectate.
            // Ölü Runner hem hedef havuzundan hem senkronizasyon şartından düşer.
            if (_aliveRunnerIds.Remove(hero.OwnerClientId))
            {
                if (SpawnDirector.Instance is ISpawnDirectorRouting routing)
                    routing.RegisterTargetPool(new List<ulong>(_aliveRunnerIds));
                convergenceTracker.ForceRecheck(); // kalanlar zaten zone'daysa geçiş açılır

                // Tüm Runner'lar ölürse ANINDA Game Over (madde 5 — onaylandı).
                if (_aliveRunnerIds.Count == 0) ServerResolve(victory: false);
            }
        }

        private void ServerHandleClientDisconnected(ulong clientId)
        {
            if (_netPhase.Value is not (FinalePhase.Voting or FinalePhase.JackIn or FinalePhase.Escape)) return;

            if (clientId == _netDefenderClientId.Value)
            {
                // Systems Design (2026-07-15, madde 2): Defender disconnect akışı KİLİTLEMEZ —
                // oyun Spirit'siz devam eder (görüş/destek kaybı zorluğu doğal cezadır).
                // Voting sırasında düşerse aday havuzundan da çıkar (VotingManager bağlı
                // olmayan adaya oy kabul etmez, resolve tally'si bağlı oyuncular üzerinden).
                Debug.Log("[Finale] Defender disconnect — koşu Spirit'siz devam ediyor (madde 2).");
                return;
            }

            if (_aliveRunnerIds.Remove(clientId))
            {
                if (SpawnDirector.Instance is ISpawnDirectorRouting routing)
                    routing.RegisterTargetPool(new List<ulong>(_aliveRunnerIds));
                convergenceTracker.ForceRecheck();
                // Madde 5: koşacak Runner kalmadıysa anında Game Over.
                if (_aliveRunnerIds.Count == 0 && _netPhase.Value == FinalePhase.Escape)
                    ServerResolve(victory: false);
            }
        }

        // ------------------------------------------------------------------ Resolution

        private void ServerHandleTimerExpired()
        {
            if (_netPhase.Value != FinalePhase.Escape) return;

            // Timer doldu → bina/Core patlar, herkes ölür (§3.6).
            foreach (ulong id in NetworkManager.ConnectedClientsIds)
            {
                BaseHero hero = GetHeroOf(id);
                if (hero == null || !hero.IsAlive) continue;
                hero.TakeDamage(new DamageInfo(
                    float.MaxValue, hero.transform.position, Vector3.zero, 0f,
                    gameObject, DamageFlags.BypassModifiers | DamageFlags.Hazard));
            }

            ServerResolve(victory: false);
        }

        private void ServerResolve(bool victory)
        {
            if (_netPhase.Value == FinalePhase.Resolved) return;

            _netVictory.Value = victory;
            _netPhase.Value = FinalePhase.Resolved;

            escapeTimer.ServerStop();
            convergenceTracker.ServerWatchFloor(-1);
            UnsubscribeHeroDeaths();

            if (SpawnDirector.Instance is ISpawnDirectorRouting routing)
                routing.SetMode(SpawnDirectorMode.Normal); // kayıtlı düşmanlar temizlenir

            NetworkGameManager.Instance?.CompleteRun(); // GS-1 session state → RunComplete
        }

        // ------------------------------------------------------------------ Helpers

        /// <summary>SERVER-ONLY: Core yanındaki Defender bedeni (SpiritAbilityController kullanır).</summary>
        public BaseHero ServerGetDefenderHero() => IsServer ? _defenderHero : null;

        private BaseHero GetHeroOf(ulong clientId)
        {
            return NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) &&
                   client.PlayerObject != null &&
                   client.PlayerObject.TryGetComponent(out BaseHero hero)
                ? hero
                : null;
        }

        /// <summary>
        /// Hareket owner-authoritative (ClientNetworkTransform) olduğundan teleport hedef
        /// client'ın kendi hero'su üzerinde yapılır; pozisyon oradan replike olur.
        /// </summary>
        [Rpc(SendTo.SpecifiedInParams)]
        private void TeleportLocalHeroRpc(Vector3 position, RpcParams rpcParams = default)
        {
            NetworkObject player = NetworkManager.LocalClient?.PlayerObject;
            if (player != null) player.transform.position = position;
        }
    }
}
