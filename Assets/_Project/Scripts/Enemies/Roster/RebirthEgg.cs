using System.Collections;
using Unity.Netcode;
using UnityEngine;
using CBuilding.Core;

namespace CBuilding.Enemies.Roster
{
    /// <summary>
    /// Phoenix-Ghoul: on lethal damage, instead of dying it collapses into a "Fiery Egg"
    /// for 2.5 seconds — inert (brain suspended) but damageable. Burst the egg and it dies
    /// for real; fail and it rebirths at 100% HP. Once per life (an egg that hatches can't
    /// re-egg — otherwise it soft-locks the fight).
    ///
    /// Implemented as an IDeathInterceptor: RosterEnemy.Die() asks us first, we restore a
    /// small egg-HP pool and take over. Visual swap is replicated via ClientRpc; both
    /// visuals reset locally on every (pooled) spawn.
    /// </summary>
    [RequireComponent(typeof(RosterEnemy))]
    public class RebirthEgg : NetworkBehaviour, IDeathInterceptor
    {
        [Min(0.5f)] [SerializeField] private float eggDuration = 2.5f;

        [Tooltip("The egg's burstable HP pool while vulnerable.")]
        [Min(1f)] [SerializeField] private float eggHealth = 150f;

        [Header("Visuals (optional)")]
        [Tooltip("Normal body root — hidden during the egg phase.")]
        [SerializeField] private GameObject normalVisual;
        [Tooltip("Egg visual root — shown during the egg phase.")]
        [SerializeField] private GameObject eggVisual;

        private bool _usedThisLife;
        private Coroutine _routine;

        public override void OnNetworkSpawn()
        {
            if (IsServer) _usedThisLife = false; // Pool-safe.
            ApplyVisual(false);                  // Every peer resets the look locally.
        }

        public bool TryInterceptDeath(RosterEnemy enemy)
        {
            if (!IsServer || _usedThisLife) return false;
            _usedThisLife = true;

            enemy.ServerRestoreHealth(eggHealth);
            enemy.SetBrainSuspended(true);
            EggStateClientRpc(true); // Runs on host too — visuals swap everywhere.

            CombatLogManager.LogAction(enemy.DisplayName, "collapsed into", "Fiery Egg",
                transform.position);

            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(EggRoutine(enemy));
            return true;
        }

        private IEnumerator EggRoutine(RosterEnemy enemy)
        {
            yield return new WaitForSeconds(eggDuration);
            _routine = null;

            if (!enemy.IsAlive) yield break; // Egg was burst — real death already ran.

            enemy.ServerRestoreHealth(enemy.MaxHealth);
            enemy.SetBrainSuspended(false);
            EggStateClientRpc(false);

            CombatLogManager.LogAction(enemy.DisplayName, "was", "reborn at full health",
                transform.position);
        }

        [ClientRpc]
        private void EggStateClientRpc(bool egg) => ApplyVisual(egg);

        private void ApplyVisual(bool egg)
        {
            if (normalVisual != null) normalVisual.SetActive(!egg);
            if (eggVisual != null) eggVisual.SetActive(egg);
        }
    }
}
