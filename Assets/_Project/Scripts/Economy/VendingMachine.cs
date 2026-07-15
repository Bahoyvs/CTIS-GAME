using System;
using CBuilding.Core;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Economy
{
    /// <summary>
    /// GS-2 shared economy — the networked Pixel Point pool that summons the section boss.
    ///
    /// AUTHORITY MODEL (same "API validates, client renders" contract as BaseHero):
    ///   - The pool and the threshold flag are NetworkVariables: server-write, everyone-read.
    ///   - Players deposit via a server RPC; the server clamps and applies.
    ///   - Crossing the threshold fires OnBossThresholdReached ON THE SERVER exactly once
    ///     per section — the boss-spawn glue (SpawnDirector special pool / boss encounter)
    ///     subscribes there. UI on all peers reads the replicated values instead.
    ///
    /// TRUST NOTE: per current design players hold Pixel Points LOCALLY, so the deposit
    /// amount is a client claim — the server can only sanity-clamp it (maxDepositPerCall).
    /// When a server-side wallet lands (planned), validate against it in DepositRpc and
    /// this note dies.
    ///
    /// SETUP: scene object with NetworkObject + trigger Collider for interaction range.
    /// Player interaction code calls DepositLocal(amount).
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class VendingMachine : NetworkBehaviour
    {
        [Header("Economy")]
        [Tooltip("Pool total that triggers the section boss.")]
        [SerializeField, Min(1)] private int requiredPointsForBoss = 100;

        [Tooltip("Sanity cap per deposit RPC — wallets are client-side today (see trust note).")]
        [SerializeField, Min(1)] private int maxDepositPerCall = 500;

        [Tooltip("Reset pool + threshold automatically when the section changes " +
                 "(each section earns its own boss).")]
        [SerializeField] private bool resetOnSectionChange = true;

        private readonly NetworkVariable<int> _netSharedPoints = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _netThresholdReached = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Every peer: pool changed — (current, required). Drives the fill bar UI.</summary>
        public event Action<int, int> OnPointsChanged;

        /// <summary>Every peer: threshold state changed (true = boss incoming). UI/audio sting hook.</summary>
        public event Action<bool> OnThresholdStateChanged;

        /// <summary>
        /// SERVER-ONLY, once per section: pool is full → spawn the boss. The SpawnDirector
        /// glue subscribes here (boss pools are biome-driven data — BiomeBossTable, GDD §5).
        /// </summary>
        public event Action OnBossThresholdReached;

        public int SharedPixelPoints => _netSharedPoints.Value;
        public int RequiredPointsForBoss => requiredPointsForBoss;
        public bool ThresholdReached => _netThresholdReached.Value;

        // ---------------------------------------------------------------- Lifecycle

        public override void OnNetworkSpawn()
        {
            _netSharedPoints.OnValueChanged += HandlePointsChanged;
            _netThresholdReached.OnValueChanged += HandleThresholdChanged;

            if (IsServer && resetOnSectionChange)
                SectionManager.OnSectionChanged += ServerHandleSectionChanged;

            // Initial paint for late joiners.
            OnPointsChanged?.Invoke(_netSharedPoints.Value, requiredPointsForBoss);
        }

        public override void OnNetworkDespawn()
        {
            _netSharedPoints.OnValueChanged -= HandlePointsChanged;
            _netThresholdReached.OnValueChanged -= HandleThresholdChanged;

            if (IsServer && resetOnSectionChange)
                SectionManager.OnSectionChanged -= ServerHandleSectionChanged;
        }

        private void HandlePointsChanged(int previous, int current) =>
            OnPointsChanged?.Invoke(current, requiredPointsForBoss);

        private void HandleThresholdChanged(bool previous, bool current) =>
            OnThresholdStateChanged?.Invoke(current);

        // ---------------------------------------------------------------- Deposit flow

        /// <summary>Player interaction code (owner-side) calls this. Validation is server-side.</summary>
        public void DepositLocal(int amount) => DepositRpc(amount);

        [Rpc(SendTo.Server)]
        private void DepositRpc(int amount, RpcParams rpcParams = default)
        {
            if (_netThresholdReached.Value) return;              // boss already summoned
            if (amount <= 0) return;                             // never trust client ints
            amount = Mathf.Min(amount, maxDepositPerCall);       // sanity cap (see trust note)

            _netSharedPoints.Value += amount;

            if (_netSharedPoints.Value >= requiredPointsForBoss)
            {
                _netThresholdReached.Value = true;
                OnBossThresholdReached?.Invoke();
            }
        }

        // ---------------------------------------------------------------- Section reset (SERVER)

        private void ServerHandleSectionChanged(int section) => ServerReset();

        /// <summary>Server-only. Fresh pool for a fresh section (or manual/debug reset).</summary>
        public void ServerReset()
        {
            if (!IsServer) return;
            _netSharedPoints.Value = 0;
            _netThresholdReached.Value = false;
        }
    }
}
