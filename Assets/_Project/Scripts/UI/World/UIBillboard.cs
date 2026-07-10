using UnityEngine;

namespace CBuilding.UI
{
    public class UIBillboard : MonoBehaviour
    {
        private Camera _mainCamera;

        private void Start()
        {
            _mainCamera = Camera.main;

            // Eğer sahnedeki ana kamera bulunamazsa hata vermemesi için
            if (_mainCamera == null)
            {
                Debug.LogWarning("UIBillboard: Sahnede 'MainCamera' etiketli bir kamera bulunamadı!");
            }
        }

        // Karakterin tüm hareket ve dönüş (animasyon) işlemleri bittikten SONRA
        // çalışması için Update yerine mutlaka LateUpdate kullanıyoruz.
        private void LateUpdate()
        {
            if (_mainCamera != null)
            {
                // Kameraya bakmak (LookAt) yerine, rotasyonunu birebir kopyala
                transform.rotation = _mainCamera.transform.rotation;
            }
        }
    }
}