using System;
using CBuilding.Heroes;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Finale
{
    /// <summary>
    /// Ruh Yeteneği'nin kahraman bazlı içeriği (Final Passive'in elle tetiklenebilir
    /// varyantı). Her hero prefab'ına, kendi kitine uygun bir implementasyon eklenir —
    /// içerik tablosu doküman Bölüm 8 ile gelecek (Kerem / Systems Design).
    /// </summary>
    public interface ISpiritAbilityEffect
    {
        /// <summary>Karaktere göre değişen cooldown (sn) — Systems Design kararı (2026-07-15, madde 7).</summary>
        float SpiritAbilityCooldown { get; }

        /// <summary>SERVER-ONLY. spiritBody = Core yanında bırakılan Defender bedeni.</summary>
        void ServerActivateSpiritAbility(BaseHero spiritBody);
    }

    /// <summary>
    /// Tek "Ruh Yeteneği" — doküman §3.4 + Systems Design cevapları (2026-07-15):
    ///
    ///   - Spirit'in HP'si YOK; run boyunca her zaman aktiftir (madde 1). HUD'daki bar
    ///     bir can barı değil, SKILL KULLANIMINDA TÜKENEN Ruh Enerjisi barıdır —
    ///     bu component'in Energy/MaxEnergy değerlerinden beslenir.
    ///   - Cooldown karaktere göre değişir (madde 7): hero'daki ISpiritAbilityEffect
    ///     kendi cooldown'unu bildirir; component'teki alan yalnızca fallback'tir.
    ///
    /// Akış: Defender client TryActivateLocal → server validasyon (defender mı, faz doğru mu,
    /// cooldown bitti mi, enerji yetiyor mu) → enerji düş + hero'daki ISpiritAbilityEffect →
    /// tüm peer'lere activated bildirimi.
    ///
    /// SETUP: Finale sahnesinde NetworkObject'li GameObject; HUD, Energy/MaxEnergy ve
    /// CooldownRemaining/OnActivated'a bağlanır (doküman §3.5).
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class SpiritAbilityController : NetworkBehaviour
    {
        public static SpiritAbilityController Instance { get; private set; }

        [Header("Ruh Enerjisi (madde 1: skill kullanımında tükenen bar)")]
        [Tooltip("Barın tam dolu değeri. Escape başında full başlar.")]
        [SerializeField, Min(1f)] private float maxEnergy = 100f;

        [Tooltip("Tek aktivasyonun enerji bedeli. maxEnergy/cost = koşu başına kullanım sayısı.")]
        [SerializeField, Min(1f)] private float energyCostPerUse = 34f;

        [Tooltip("Saniyede pasif dolum. 0 = hiç dolmaz (bar sadece tükenir — mevcut tasarım).")]
        [SerializeField, Min(0f)] private float energyRegenPerSecond = 0f;

        [Header("Cooldown")]
        [Tooltip("FALLBACK cooldown (sn) — hero'da ISpiritAbilityEffect yoksa kullanılır. " +
                 "Gerçek değer karaktere göre effect'ten okunur (madde 7).")]
        [SerializeField, Min(1f)] private float fallbackCooldown = 90f;

        // Hazır olma anı ServerTime cinsinden — HUD kalan süreyi lokal hesaplar.
        private readonly NetworkVariable<double> _netReadyServerTime = new(
            0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _netEnergy = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Her peer'de: yetenek tetiklendi (VFX/UI sinyali, §3.5 "net UI sinyali").</summary>
        public event Action OnActivated;

        /// <summary>HUD Ruh Enerjisi barı bu ikisinden beslenir (HP barı DEĞİL).</summary>
        public float Energy => _netEnergy.Value;
        public float MaxEnergy => maxEnergy;

        public float CooldownRemaining =>
            Mathf.Max(0f, (float)(_netReadyServerTime.Value - NetworkManager.ServerTime.Time));

        public bool IsReady => CooldownRemaining <= 0f && _netEnergy.Value >= energyCostPerUse;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        private void Update()
        {
            // Opsiyonel pasif dolum (server). Varsayılan 0 — bar sadece kullanımla tükenir.
            if (!IsServer || energyRegenPerSecond <= 0f) return;
            if (FinaleManager.Instance == null ||
                FinaleManager.Instance.CurrentPhase != FinalePhase.Escape) return;
            if (_netEnergy.Value < maxEnergy)
                _netEnergy.Value = Mathf.Min(maxEnergy, _netEnergy.Value + energyRegenPerSecond * Time.deltaTime);
        }

        /// <summary>FinaleManager, Escape fazına girerken çağırır: bar full başlar.</summary>
        public void ServerResetForEscape()
        {
            if (!IsServer) return;
            _netEnergy.Value = maxEnergy;
            _netReadyServerTime.Value = 0d;
        }

        // ---------------------------------------------------------------- Client entry

        /// <summary>Defender'ın input/UI'ı çağırır. Validasyon server'da tekrarlanır.</summary>
        public void TryActivateLocal()
        {
            FinaleManager finale = FinaleManager.Instance;
            if (finale == null || !IsReady) return;
            if (finale.DefenderClientId != NetworkManager.LocalClientId) return;
            RequestActivateRpc();
        }

        [Rpc(SendTo.Server)]
        private void RequestActivateRpc(RpcParams rpcParams = default)
        {
            FinaleManager finale = FinaleManager.Instance;
            if (finale == null) return;

            ulong sender = rpcParams.Receive.SenderClientId;
            if (sender != finale.DefenderClientId) return;                    // sadece Spirit
            if (finale.CurrentPhase != FinalePhase.Escape) return;            // koşu sırasında
            if (CooldownRemaining > 0f) return;
            if (_netEnergy.Value < energyCostPerUse) return;                  // bar tükendi

            _netEnergy.Value -= energyCostPerUse;

            // Kahraman bazlı içerik + karaktere göre cooldown (madde 7).
            // Henüz eklenmemiş hero'larda güvenli no-op + fallback cooldown (Bölüm 8 bekleniyor).
            BaseHero body = finale.ServerGetDefenderHero();
            float cooldown = fallbackCooldown;
            if (body != null && body.TryGetComponent(out ISpiritAbilityEffect effect))
            {
                cooldown = Mathf.Max(1f, effect.SpiritAbilityCooldown);
                effect.ServerActivateSpiritAbility(body);
            }
            else
            {
                Debug.LogWarning("[SpiritAbility] Defender hero'da ISpiritAbilityEffect yok — " +
                                 "kahraman bazlı Ruh Yeteneği içeriği (Bölüm 8) henüz bağlanmamış.");
            }

            _netReadyServerTime.Value = NetworkManager.ServerTime.Time + cooldown;
            ActivatedClientRpc();
        }

        [ClientRpc]
        private void ActivatedClientRpc() => OnActivated?.Invoke();
    }
}
