using CBuilding.Enemies;
using CBuilding.Heroes;
using CBuilding.StatusEffects;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Heroes.Bahadir
{
    /// <summary>
    /// "Enemies can't see him" from the Feature design doc. BaseEnemy.SelectTarget()
    /// already refuses to ACQUIRE a stealthed hero as a new target, but on its own that
    /// only stops NEW targeting — an enemy that already had Bahadır as CurrentTarget would
    /// otherwise keep chasing/attacking him until the next retargetInterval tick (up to
    /// 0.5s later) naturally re-evaluates and drops him. This forces every enemy currently
    /// locked onto Bahadır to drop him the INSTANT Stealth turns on.
    ///
    /// SERVER-ONLY: BaseEnemy.CurrentTarget and all AI state only exist meaningfully on the
    /// server (clients are "dumb terminals" for enemies — see BaseEnemy's own docs). Gated
    /// on IsServer, not IsOwner, so this still runs correctly on a dedicated server where
    /// nobody owns Bahadır's hero from the server's own perspective.
    /// </summary>
    [RequireComponent(typeof(HeroController))]
    public class BahadirStealthGuard : NetworkBehaviour
    {
        private HeroController _hero;
        private StatusEffectController _status;

        private void Awake()
        {
            _hero = GetComponent<HeroController>();
            _status = GetComponent<StatusEffectController>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer || _status == null) { enabled = false; return; }
            _status.OnControlFlagsChanged += HandleControlFlagsChanged;
        }

        public override void OnNetworkDespawn()
        {
            if (_status != null) _status.OnControlFlagsChanged -= HandleControlFlagsChanged;
        }

        private void HandleControlFlagsChanged(ControlFlags previous, ControlFlags current)
        {
            bool wasStealthed = (previous & ControlFlags.Stealth) != 0;
            bool isStealthed = (current & ControlFlags.Stealth) != 0;
            if (!isStealthed || wasStealthed) return; // only react to the OFF->ON edge

            foreach (BaseEnemy enemy in FindObjectsByType<BaseEnemy>(FindObjectsSortMode.None))
            {
                if (enemy != null) enemy.ForceDropTarget(_hero);
            }
        }
    }
}
