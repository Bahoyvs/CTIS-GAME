using System;
using System.Collections.Generic;
using UnityEngine;

namespace CBuilding.Finale
{
    /// <summary>
    /// Kat geçiş senkronizasyonu (doküman §3.3 + Entegrasyon Notu 2):
    /// izlenen kattaki bir Convergence Zone, HAYATTAKİ tüm Runner'ları aynı anda
    /// içerdiğinde OnFloorConverged ateşlenir. Ölü oyuncular şarttan otomatik muaf —
    /// hayatta-Runner listesini FinaleManager sağlar (provider delege).
    ///
    /// Tamamen server-side; FinaleManager configure edip fazla birlikte açar/kapar.
    /// </summary>
    public class FloorConvergenceTracker : MonoBehaviour
    {
        /// <summary>SERVER-ONLY: izlenen kat converge oldu (payload: kat indeksi).</summary>
        public event Action<int> OnFloorConverged;

        private Func<IReadOnlyCollection<ulong>> _aliveRunnersProvider;
        private int _watchedFloor = -1;
        private bool _armed;

        private void OnEnable() => ConvergenceZone.OnAnyOccupancyChanged += HandleOccupancyChanged;
        private void OnDisable() => ConvergenceZone.OnAnyOccupancyChanged -= HandleOccupancyChanged;

        /// <summary>FinaleManager, Escape fazına girerken çağırır.</summary>
        public void ServerConfigure(Func<IReadOnlyCollection<ulong>> aliveRunnersProvider)
        {
            _aliveRunnersProvider = aliveRunnersProvider;
        }

        /// <summary>Bu kat için toplanma bekle. -1 = takip kapalı.</summary>
        public void ServerWatchFloor(int floorIndex)
        {
            _watchedFloor = floorIndex;
            _armed = floorIndex >= 0;
            ForceRecheck(); // takım zaten zone'da bekliyor olabilir (hızlı koşular)
        }

        /// <summary>Runner ölümü gibi doluluk DIŞI değişimlerde FinaleManager çağırır.</summary>
        public void ForceRecheck()
        {
            if (!_armed || _aliveRunnersProvider == null) return;

            IReadOnlyCollection<ulong> alive = _aliveRunnersProvider();
            if (alive == null || alive.Count == 0) return; // herkes öldü — kayıp akışı FinaleManager'da

            foreach (ConvergenceZone zone in ConvergenceZone.ActiveZones)
            {
                if (zone.FloorIndex != _watchedFloor) continue;
                if (!ContainsAll(zone, alive)) continue;

                _armed = false; // aynı kat için çift tetiklemeyi engelle
                OnFloorConverged?.Invoke(_watchedFloor);
                return;
            }
        }

        private void HandleOccupancyChanged(ConvergenceZone zone)
        {
            if (_armed && zone.FloorIndex == _watchedFloor) ForceRecheck();
        }

        private static bool ContainsAll(ConvergenceZone zone, IReadOnlyCollection<ulong> ids)
        {
            foreach (ulong id in ids)
                if (!zone.Contains(id)) return false;
            return true;
        }
    }
}
