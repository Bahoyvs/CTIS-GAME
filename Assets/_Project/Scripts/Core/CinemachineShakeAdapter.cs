// ---------------------------------------------------------------------------
// Cinemachine (com.unity.cinemachine, 3.x) is installed and the CINEMACHINE
// scripting define is set (Project Settings -> Player -> Scripting Define
// Symbols, Standalone target) — this adapter compiles and runs.
//
// Scene wiring (already done on Main Camera): this component + a
// CinemachineImpulseSource on the same GameObject that receives the shake
// (Main Camera), plus a CinemachineExternalImpulseListener so it actually
// reacts to impulses fired by any source. If you ever see this code compiled
// out (greyed in the IDE), the CINEMACHINE define got dropped — re-add it.
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
