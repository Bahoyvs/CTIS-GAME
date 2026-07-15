using CBuilding.Heroes;
using Unity.Cinemachine; // CM2 projelerinde: using Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CBuilding.Finale
{
    /// <summary>
    /// Spirit (Ruh) kamera/görüş sistemi — doküman §3.4. Bu bir hack/network kurgusu
    /// DEĞİL; eski "God's Eye" tasarımının yerini alır:
    ///
    ///   - Normal izometrik açı korunur; serbest hareket (free-cam) sadece Runner'ların
    ///     o an bulunduğu KATIN FinaleFloorBounds hacmiyle sınırlı.
    ///   - Kat, FinaleManager.CurrentFloor NetworkVariable'ından okunur — Runner'lar kat
    ///     değiştirince Spirit'in alanı otomatik güncellenir (ayrı RPC gerekmez).
    ///   - Monokrom "ruhani" görüş: spiritVisionRoot (Volume/post-process kökü) toggle'ı.
    ///     Shader'ın kendisi art-side; buradan sadece açılır/kapanır.
    ///   - Fog of war: IsPointRevealed() net görüş menzilini Runner yakınlığıyla sınırlar;
    ///     görüş shader'ı / minimap bu sorguyu kullanır.
    ///
    /// Yalnızca Defender'ın client'ında aktifleşir (FinaleManager faz + defenderId'den).
    /// SETUP: Finale sahnesine bir adet; spiritCam = MainIsoCam'den ayrı bir
    /// CinemachineCamera (Follow = bu component'in ürettiği anchor).
    /// </summary>
    public class SpiritVisionController : MonoBehaviour
    {
        [Header("Camera")]
        [Tooltip("Spirit'e özel vcam. Priority, aktifken MainIsoCam'in üzerine çıkarılır.")]
        [SerializeField] private CinemachineCamera spiritCam;

        [Tooltip("MainIsoCam'i yenmesi için aktif Priority (CameraModeController.ownedPriority=20'den büyük).")]
        [SerializeField] private int activePriority = 30;

        [SerializeField] private int inactivePriority = 0;

        [Header("Movement")]
        [SerializeField, Min(1f)] private float moveSpeed = 14f;

        [Header("Vision")]
        [Tooltip("Monokrom ruhani görüş post-process kökü (Volume vb.). Aktif fazda açılır.")]
        [SerializeField] private GameObject spiritVisionRoot;

        [Tooltip("Bir noktanın 'net görünür' sayılması için en yakın hayattaki Runner'a azami mesafe.")]
        [SerializeField, Min(1f)] private float revealRadiusAroundRunners = 18f;

        private Transform _anchor; // free-cam'in takip hedefi
        private bool _active;

        public bool IsActive => _active;

        private void Awake()
        {
            _anchor = new GameObject("SpiritCamAnchor").transform;
            _anchor.SetParent(transform, false);
            if (spiritCam != null)
            {
                spiritCam.Follow = _anchor;
                spiritCam.Priority = inactivePriority;
            }
        }

        private void Update()
        {
            RefreshActivation();
            if (!_active) return;

            MoveAnchor();
            ClampToCurrentFloor();
        }

        // ---------------------------------------------------------------- Activation

        private void RefreshActivation()
        {
            bool shouldBeActive = false;

            FinaleManager finale = FinaleManager.Instance;
            NetworkManager nm = NetworkManager.Singleton;
            if (finale != null && nm != null && nm.IsClient)
            {
                bool isDefender = finale.DefenderClientId == nm.LocalClientId;
                bool spiritPhase = finale.CurrentPhase == FinalePhase.JackIn ||
                                   finale.CurrentPhase == FinalePhase.Escape;
                shouldBeActive = isDefender && spiritPhase;
            }

            if (shouldBeActive == _active) return;
            _active = shouldBeActive;

            if (spiritCam != null)
                spiritCam.Priority = _active ? activePriority : inactivePriority;
            if (spiritVisionRoot != null)
                spiritVisionRoot.SetActive(_active);

            if (_active && FinaleFloorBounds.TryGetBounds(CurrentFloor, out Bounds b))
                _anchor.position = b.center; // kata girişte merkezden başla
        }

        private static int CurrentFloor =>
            FinaleManager.Instance != null ? FinaleManager.Instance.CurrentFloor : FinaleFloors.Basement;

        // ---------------------------------------------------------------- Free-cam

        private void MoveAnchor()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            Vector2 input = Vector2.zero;
            if (kb.wKey.isPressed) input.y += 1f;
            if (kb.sKey.isPressed) input.y -= 1f;
            if (kb.dKey.isPressed) input.x += 1f;
            if (kb.aKey.isPressed) input.x -= 1f;
            if (input.sqrMagnitude < 0.01f) return;

            // İzometrik eksende hareket: kamera yönüne göre XZ düzlemi.
            Vector3 fwd = spiritCam != null ? spiritCam.transform.forward : Vector3.forward;
            fwd.y = 0f; fwd.Normalize();
            Vector3 right = new(fwd.z, 0f, -fwd.x);

            _anchor.position += (fwd * input.y + right * input.x).normalized
                                * (moveSpeed * Time.deltaTime);
        }

        private void ClampToCurrentFloor()
        {
            if (!FinaleFloorBounds.TryGetBounds(CurrentFloor, out Bounds b)) return;
            Vector3 p = _anchor.position;
            _anchor.position = new Vector3(
                Mathf.Clamp(p.x, b.min.x, b.max.x),
                Mathf.Clamp(p.y, b.min.y, b.max.y),
                Mathf.Clamp(p.z, b.min.z, b.max.z));
        }

        // ---------------------------------------------------------------- Fog of war query

        /// <summary>
        /// Nokta, hayattaki herhangi bir Runner'a revealRadius içinde mi? Görüş shader'ı,
        /// silüet/minimap sistemleri bu sorguyla "takımdan uzak alanlar karanlık" kuralını uygular.
        /// Her peer'de çalışır (BaseHero.ActiveHeroes replikasyonla dolu).
        /// </summary>
        public bool IsPointRevealed(Vector3 worldPoint)
        {
            float sqrRadius = revealRadiusAroundRunners * revealRadiusAroundRunners;
            foreach (BaseHero hero in BaseHero.ActiveHeroes)
            {
                if (hero == null || !hero.IsAlive) continue;
                if (FinaleManager.Instance != null &&
                    hero.OwnerClientId == FinaleManager.Instance.DefenderClientId) continue; // beden sayılmaz
                if ((hero.transform.position - worldPoint).sqrMagnitude <= sqrRadius) return true;
            }
            return false;
        }
    }
}
