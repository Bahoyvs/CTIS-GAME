using UnityEngine;

namespace CBuilding.Heroes
{
    /// <summary>
    /// Optional per-archetype hook into HeroController's Roll (GDD: Shift dash).
    /// Mirrors IBasicAttackBehaviour's discovery pattern: implementations are
    /// MonoBehaviours on the hero prefab, found via GetComponent&lt;IRollBehaviour&gt;()
    /// in HeroController.Awake() — no Inspector dropdown, no empty no-op methods
    /// forced onto heroes whose kit doesn't touch Roll.
    ///
    /// Roll's own movement (distance/speed/timing/animation lock) stays entirely in
    /// HeroController/BaseHero — implementations REACT to a roll, they never drive it
    /// or override _rollDirection/_rollTimeRemaining.
    /// </summary>
    public interface IRollBehaviour
    {
        /// <summary>
        /// OWNER-side. Called once, the instant a roll is accepted (before that frame's
        /// movement is applied). If the modification needs to touch server state
        /// (status effects, damage), send a ServerRpc from here — same "owner requests,
        /// server validates" contract as every other input entry point in HeroController.
        /// </summary>
        void OnRollStart(HeroController hero, Vector3 rollDirection);

        /// <summary>OWNER-side. Called once, the frame the roll's duration elapses.</summary>
        void OnRollEnd(HeroController hero);
    }
}
