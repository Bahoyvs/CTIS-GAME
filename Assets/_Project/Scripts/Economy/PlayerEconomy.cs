using System;
using CBuilding.UI;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Economy
{
    /// <summary>
    /// Per-player Pixel Point wallet. Sits next to BaseHero on the hero prefab.
    ///
    /// AUTHORITY MODEL (inverse of health — this is the one OWNER-authoritative stat):
    ///   - PixelPoints is a NetworkVariable: OWNER-write, everyone-read. Think of it as a
    ///     row the client owns and replicates outward, like an offline-first local cache
    ///     that syncs up — teammates' HUDs read the replica, only the owner mutates.
    ///   - The server can't write it directly (write permission is Owner), so server-side
    ///     grants (loot pickups) arrive via GrantPointsRpc(SendTo.Owner): the server tells
    ///     the owning client "credit yourself N", and the owner applies it.
    ///
    /// TRUST NOTE: this is the client-side wallet VendingMachine's trust note refers to —
    /// deposits into the shared pool remain a client claim until a server-side ledger lands.
    /// Acceptable for 4-player co-op MVP.
    ///
    /// VENDING INTEGRATION (owner-side interaction code):
    ///     if (economy.TrySpend(amount)) vendingMachine.DepositLocal(amount);
    /// TrySpend = check + RemovePoints in one call so you can't deposit points you don't have.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerEconomy : NetworkBehaviour
    {
        // Owner-write is the whole design: pickups feel instant for the owner (no server
        // round-trip before the HUD ticks up), teammates get the delta-synced replica.
        private readonly NetworkVariable<int> _netPixelPoints = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>Every peer: wallet changed — (current). Drives the local HUD counter.</summary>
        public event Action<int> OnPointsChanged;

        public int PixelPoints => _netPixelPoints.Value;

        // ---------------------------------------------------------------- Lifecycle

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            _netPixelPoints.OnValueChanged += HandlePointsChanged;

            if (IsOwner)
            {
                if (PlayerEconomyUI.Instance != null)
                {
                    PlayerEconomyUI.Instance.UpdatePointsDisplay(PixelPoints);
                }

                OnPointsChanged += HandleLocalUIUpdate;
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            _netPixelPoints.OnValueChanged -= HandlePointsChanged;

            if (IsOwner)
            {
                OnPointsChanged -= HandleLocalUIUpdate;
            }
        }

        private void HandlePointsChanged(int previous, int current) =>
            OnPointsChanged?.Invoke(current);

        private void HandleLocalUIUpdate(int newTotal)
        {
            if (PlayerEconomyUI.Instance != null)
            {
                PlayerEconomyUI.Instance.UpdatePointsDisplay(newTotal);
            }
        }

        // ---------------------------------------------------------------- Owner-side mutations

        /// <summary>OWNER-ONLY. Credit the wallet. Returns false when called off-owner.</summary>
        public bool AddPoints(int amount)
        {
            if (!IsOwner || amount <= 0) return false;
            _netPixelPoints.Value += amount;
            return true;
        }

        /// <summary>OWNER-ONLY. Debit the wallet. Fails (no partial debit) on insufficient funds.</summary>
        public bool RemovePoints(int amount)
        {
            if (!IsOwner || amount <= 0) return false;
            if (_netPixelPoints.Value < amount) return false;
            _netPixelPoints.Value -= amount;
            return true;
        }

        /// <summary>
        /// OWNER-ONLY convenience for the vending machine flow: balance check + debit as one
        /// atomic op. Caller does: if (TrySpend(x)) vendingMachine.DepositLocal(x);
        /// </summary>
        public bool TrySpend(int amount) => RemovePoints(amount);

        // ---------------------------------------------------------------- Server → owner grant

        /// <summary>
        /// Called by SERVER-side code (PixelPointPickup). SendTo.Owner routes this to the
        /// owning client only — NGO's targeted-RPC equivalent of RpcTarget.Single, but
        /// resolved from this object's ownership, so it can't hit the wrong player.
        /// The owner then writes its own owner-authoritative NetworkVariable.
        /// </summary>
        [Rpc(SendTo.Owner)]
        public void GrantPointsRpc(int amount)
        {
            AddPoints(amount);
        }
    }
}
