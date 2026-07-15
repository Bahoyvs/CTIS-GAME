using System;
using System.Collections.Generic;
using CBuilding.Heroes;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Finale
{
    /// <summary>
    /// Kat geçişi için toplanma alanı (trigger volume). Her katta bir tane; çatıdaki
    /// (floorIndex = 4) aynı zamanda extraction alanıdır.
    ///
    /// Doluluk yalnızca SERVER'da takip edilir (host fiziği otoritedir); client kopyaları
    /// sadece görsel/LD amaçlıdır. FloorConvergenceTracker statik event üzerinden dinler.
    ///
    /// SETUP: isTrigger Collider + bu component. NetworkObject GEREKMEZ.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ConvergenceZone : MonoBehaviour
    {
        /// <summary>Sahnedeki aktif zone'lar (server-side sorgular için).</summary>
        public static readonly List<ConvergenceZone> ActiveZones = new();

        /// <summary>Herhangi bir zone'un doluluğu değişti (server-only ateşlenir).</summary>
        public static event Action<ConvergenceZone> OnAnyOccupancyChanged;

        [Tooltip("0 = Bodrum, 4 = Çatı (extraction).")]
        [SerializeField, Range(0, 4)] private int floorIndex;

        private readonly HashSet<ulong> _occupants = new(); // server-only

        public int FloorIndex => floorIndex;
        public IReadOnlyCollection<ulong> Occupants => _occupants;

        public bool Contains(ulong clientId) => _occupants.Contains(clientId);

        private void OnEnable() => ActiveZones.Add(this);

        private void OnDisable()
        {
            ActiveZones.Remove(this);
            _occupants.Clear();
        }

        private static bool IsServer =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer) return;
            var hero = other.GetComponentInParent<BaseHero>();
            if (hero == null) return;
            if (_occupants.Add(hero.OwnerClientId))
                OnAnyOccupancyChanged?.Invoke(this);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsServer) return;
            var hero = other.GetComponentInParent<BaseHero>();
            if (hero == null) return;
            if (_occupants.Remove(hero.OwnerClientId))
                OnAnyOccupancyChanged?.Invoke(this);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.35f);
            if (TryGetComponent(out BoxCollider box))
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawCube(box.center, box.size);
            }
        }
#endif
    }
}
