using UnityEngine;

namespace CBuilding.Utilities
{
    /// <summary>
    /// Keeps a sprite plane facing the isometric camera (HD-2D staple). Attach to the
    /// sprite child of a character, not the logic root — the root's transform stays
    /// rotation-free so movement/physics math is never affected by visual rotation.
    /// Runs in LateUpdate so it applies after all movement for the frame.
    /// </summary>
    [DisallowMultipleComponent]
    public class SpriteBillboard : MonoBehaviour
    {
        [Tooltip("Match the camera's downward tilt too (full billboard). If false, only " +
                 "matches yaw — sprite stays vertical, which reads better for characters " +
                 "standing on the grid.")]
        [SerializeField] private bool matchCameraTilt = false;

        private Transform _camTransform;

        private void Start()
        {
            if (Camera.main != null) _camTransform = Camera.main.transform;
        }

        private void LateUpdate()
        {
            if (_camTransform == null) return;

            if (matchCameraTilt)
            {
                // Fully mirror camera orientation (classic billboard).
                transform.rotation = _camTransform.rotation;
            }
            else
            {
                // Yaw only: with a static isometric camera this is constant, but keeping it
                // live supports camera rotation later for free. Cost is negligible.
                Vector3 euler = _camTransform.rotation.eulerAngles;
                transform.rotation = Quaternion.Euler(0f, euler.y, 0f);
            }
        }
    }
}
