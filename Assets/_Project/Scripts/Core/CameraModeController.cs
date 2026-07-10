using Unity.Netcode;
using Unity.Cinemachine; // CM2 projects: using Cinemachine;
using UnityEngine;

namespace CBuilding.Core
{
    /// <summary>
    /// Per-player activation for the shared "MainIsoCam" virtual camera.
    ///
    /// WHY FindFirstObjectByType INSTEAD OF AN INSPECTOR REFERENCE: this component lives on
    /// the hero prefab, which is instantiated over the network (see PlayerSpawner). A prefab
    /// asset cannot hold a serialized reference to a scene object (MainIsoCam lives in the
    /// gameplay scene, not the prefab), so the link has to be resolved at runtime instead.
    ///
    /// WHY OWNER-ONLY: every peer spawns a copy of every hero (that's how NGO replication
    /// works), so without the IsOwner gate every client would fight over Follow/Priority on
    /// the one shared vcam. Only the local player's copy is allowed to touch it.
    ///
    /// SCALING NOTE: FindFirstObjectByType assumes exactly one CinemachineCamera in the scene.
    /// If cutscene/ability-zoom vcams are added later, replace this with a tagged lookup or a
    /// small static registry (e.g. a MainIsoCam singleton) instead of type-based search.
    /// </summary>
    public class CameraModeController : NetworkBehaviour
    {
        [Tooltip("Priority given to MainIsoCam while this hero owns it. Must be higher than " +
                 "MainIsoCam's own idle Priority (set in the Inspector, e.g. 10) so ownership " +
                 "always wins.")]
        [SerializeField] private int ownedPriority = 20;

        private CinemachineCamera _mainIsoCam;

        public override void OnNetworkSpawn()
        {
            // Remote copies of other players' heroes never touch the camera — only the
            // client that owns this hero does.
            if (!IsOwner)
            {
                enabled = false;
                return;
            }

            _mainIsoCam = FindFirstObjectByType<CinemachineCamera>();
            if (_mainIsoCam == null)
            {
                Debug.LogError("[CameraModeController] Sahnede MainIsoCam (CinemachineCamera) bulunamadı.", this);
                return;
            }

            _mainIsoCam.Follow = transform;
            _mainIsoCam.Priority = ownedPriority;
        }

        public override void OnNetworkDespawn()
        {
            // Guard against clobbering a DIFFERENT hero's claim: only release Follow if this
            // hero is still the one it points at (e.g. avoids a race where this fires after
            // a respawn has already reassigned the vcam to a new hero instance).
            if (_mainIsoCam != null && _mainIsoCam.Follow == transform)
                _mainIsoCam.Follow = null;
        }
    }
}
