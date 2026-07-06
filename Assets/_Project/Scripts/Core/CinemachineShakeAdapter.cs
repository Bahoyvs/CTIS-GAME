// ---------------------------------------------------------------------------
// Cinemachine is NOT currently in Packages/manifest.json, so this adapter is
// compiled out behind a scripting define to keep the project compiling.
//
// To enable screen shake:
//   1. Package Manager -> install "Cinemachine" (com.unity.cinemachine, 3.x).
//   2. Project Settings -> Player -> Scripting Define Symbols -> add: CINEMACHINE
//   3. Add this component + a CinemachineImpulseSource to your virtual camera
//      (and a CinemachineImpulseListener on the camera itself).
// ---------------------------------------------------------------------------
#if CINEMACHINE
using UnityEngine;
using Unity.Cinemachine; // Cinemachine 3.x namespace. For 2.x use: using Cinemachine;

namespace CBuilding.Core
{
    /// <summary>
    /// Bridges GameFeelManager's engine-agnostic shake event to a Cinemachine Impulse.
    /// Subscribing/unsubscribing in OnEnable/OnDisable prevents dangling delegates
    /// pointing at destroyed objects (the Unity equivalent of a listener leak).
    /// </summary>
    [RequireComponent(typeof(CinemachineImpulseSource))]
    public class CinemachineShakeAdapter : MonoBehaviour
    {
        [SerializeField] private float forceMultiplier = 1f;

        private CinemachineImpulseSource _impulseSource;

        private void Awake() => _impulseSource = GetComponent<CinemachineImpulseSource>();
        private void OnEnable() => GameFeelManager.OnScreenShakeRequested += HandleShake;
        private void OnDisable() => GameFeelManager.OnScreenShakeRequested -= HandleShake;

        private void HandleShake(float intensity)
        {
            _impulseSource.GenerateImpulseWithForce(intensity * forceMultiplier);
        }
    }
}
#endif
