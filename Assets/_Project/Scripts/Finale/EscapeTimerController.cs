using System;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Finale
{
    /// <summary>
    /// Phase.Escape geri sayımı. Bitiş anı ServerTime cinsinden replike edilir;
    /// client'lar kalan süreyi lokal hesaplar (her frame NetworkVariable yazmak yok).
    /// Süre dolunca (server) OnExpired → FinaleManager kaybı tetikler (bina/Core patlar).
    ///
    /// Toplam süre balans TBD (doküman §3.5) — Inspector'dan ayarlanır.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class EscapeTimerController : NetworkBehaviour
    {
        [Tooltip("Escape fazının toplam süresi (sn). Systems Design bandı: 5-10 dk " +
                 "(300-600 sn, madde 7); kesin değer balans testleriyle bu bant içinde seçilecek.")]
        [SerializeField, Range(300f, 600f)] private float escapeDuration = 420f;

        private readonly NetworkVariable<double> _netEndServerTime = new(
            0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _netRunning = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>SERVER-ONLY: süre doldu. FinaleManager abone olur.</summary>
        public event Action OnExpired;

        public bool Running => _netRunning.Value;

        /// <summary>Her peer'de geçerli — HUD Escape Timer bunu okur.</summary>
        public float Remaining => _netRunning.Value
            ? Mathf.Max(0f, (float)(_netEndServerTime.Value - NetworkManager.ServerTime.Time))
            : 0f;

        public void ServerStart(float durationOverride = -1f)
        {
            if (!IsServer) return;
            float duration = durationOverride > 0f ? durationOverride : escapeDuration;
            _netEndServerTime.Value = NetworkManager.ServerTime.Time + duration;
            _netRunning.Value = true;
        }

        /// <summary>Kazanma/erken çözülme durumunda durdurur (OnExpired ateşlenmez).</summary>
        public void ServerStop()
        {
            if (!IsServer) return;
            _netRunning.Value = false;
        }

        private void Update()
        {
            if (!IsServer || !_netRunning.Value) return;
            if (NetworkManager.ServerTime.Time >= _netEndServerTime.Value)
            {
                _netRunning.Value = false;
                OnExpired?.Invoke();
            }
        }
    }
}
