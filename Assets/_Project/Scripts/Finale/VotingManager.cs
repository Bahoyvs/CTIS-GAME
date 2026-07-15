using System;
using System.Collections.Generic;
using CBuilding.Heroes;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Finale
{
    /// <summary>
    /// Section 4 Defender (Spirit) oylaması. Server-authoritative:
    /// client'lar SubmitVoteLocal ile aday clientId gönderir, server sayar.
    ///
    /// Tie-break — GÜNCEL kural (Systems Design 2026-07-15, madde 4): en DÜŞÜK anlık HP,
    /// o da eşitse rastgele. (Dokümandaki "en yüksek HP" kuralı revize edildi: en sağlıklı
    /// oyuncuyu Runner tarafında tutmak takım gücünü korur.)
    /// Herkes oy verdiğinde ya da süre dolduğunda çözülür (oy vermeyenler çekimser).
    ///
    /// SETUP: Finale sahnesinde NetworkObject'li bir GameObject'e ekle, FinaleManager'ın
    /// Inspector alanına ata.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class VotingManager : NetworkBehaviour
    {
        public static VotingManager Instance { get; private set; }

        [Tooltip("Oylama penceresi (sn). Süre dolunca eldeki oylarla çözülür.")]
        [SerializeField, Min(5f)] private float voteDuration = 20f;

        // Herkese görünür: UI "oylama açık" panelini bundan sürer.
        private readonly NetworkVariable<bool> _netVoteActive = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Her peer'de: oylama açıldı/kapandı (UI hook).</summary>
        public event Action<bool> OnVoteActiveChanged;

        /// <summary>SERVER-ONLY: kazanan Defender'ın clientId'si. FinaleManager abone olur.</summary>
        public event Action<ulong> OnDefenderChosen;

        // ---- Server-only state ----
        private readonly Dictionary<ulong, ulong> _votes = new(); // voter -> candidate
        private float _voteEndTime;

        public bool VoteActive => _netVoteActive.Value;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            _netVoteActive.OnValueChanged += HandleActiveChanged;
        }

        public override void OnNetworkDespawn()
        {
            _netVoteActive.OnValueChanged -= HandleActiveChanged;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        private void HandleActiveChanged(bool previous, bool current) =>
            OnVoteActiveChanged?.Invoke(current);

        private void Update()
        {
            if (!IsServer || !_netVoteActive.Value) return;
            if (Time.time >= _voteEndTime || AllConnectedVoted())
                ServerResolve();
        }

        // ---------------------------------------------------------------- Server API

        /// <summary>FinaleManager çağırır. Aktif bir oylama varken no-op.</summary>
        public void ServerBeginVote()
        {
            if (!IsServer || _netVoteActive.Value) return;
            _votes.Clear();
            _voteEndTime = Time.time + voteDuration;
            _netVoteActive.Value = true;
        }

        // ---------------------------------------------------------------- Client API

        /// <summary>UI çağırır: yerel oyuncunun oyu. Kendine oy vermek serbest.</summary>
        public void SubmitVoteLocal(ulong candidateClientId) => SubmitVoteRpc(candidateClientId);

        [Rpc(SendTo.Server)]
        private void SubmitVoteRpc(ulong candidateClientId, RpcParams rpcParams = default)
        {
            if (!_netVoteActive.Value) return;
            // Aday bağlı bir oyuncu olmalı — client int'ine asla güvenme.
            if (!NetworkManager.ConnectedClients.ContainsKey(candidateClientId)) return;
            _votes[rpcParams.Receive.SenderClientId] = candidateClientId; // son oy geçerli
        }

        // ---------------------------------------------------------------- Resolution

        private bool AllConnectedVoted()
        {
            foreach (ulong id in NetworkManager.ConnectedClientsIds)
                if (!_votes.ContainsKey(id)) return false;
            return _votes.Count > 0;
        }

        private void ServerResolve()
        {
            _netVoteActive.Value = false;

            // Sayım: aday -> oy. Hiç oy yoksa tüm bağlı oyuncular 0 oyla aday sayılır
            // (tie-break zinciri yine de tek kazanan üretir).
            var tally = new Dictionary<ulong, int>();
            foreach (ulong id in NetworkManager.ConnectedClientsIds) tally[id] = 0;
            foreach (ulong candidate in _votes.Values)
                if (tally.ContainsKey(candidate)) tally[candidate]++;

            int bestVotes = -1;
            var leaders = new List<ulong>();
            foreach (var kvp in tally)
            {
                if (kvp.Value > bestVotes) { bestVotes = kvp.Value; leaders.Clear(); leaders.Add(kvp.Key); }
                else if (kvp.Value == bestVotes) leaders.Add(kvp.Key);
            }

            ulong winner = leaders.Count == 1 ? leaders[0] : BreakTie(leaders);
            OnDefenderChosen?.Invoke(winner);
        }

        /// <summary>Tie-break: en DÜŞÜK anlık HP (madde 4) → hâlâ eşitse rastgele.</summary>
        private ulong BreakTie(List<ulong> leaders)
        {
            float bestHp = float.MaxValue;
            var hpLeaders = new List<ulong>();

            foreach (ulong id in leaders)
            {
                // Hero'su bulunamayan aday, kıyasta en kötü değeri alır (asla kayırılmaz).
                float hp = float.MaxValue;
                if (NetworkManager.ConnectedClients.TryGetValue(id, out var client) &&
                    client.PlayerObject != null &&
                    client.PlayerObject.TryGetComponent(out BaseHero hero))
                {
                    hp = hero.CurrentHealth;
                }

                if (hp < bestHp) { bestHp = hp; hpLeaders.Clear(); hpLeaders.Add(id); }
                else if (Mathf.Approximately(hp, bestHp)) hpLeaders.Add(id);
            }

            return hpLeaders[UnityEngine.Random.Range(0, hpLeaders.Count)];
        }
    }
}
