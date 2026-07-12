using UnityEngine;

namespace CBuilding.Heroes
{
    /// <summary>
    /// GS-17 §6.2 — the split that lets 6 data-driven heroes and 2 bespoke Gladiators
    /// share the exact same HeroController call sites, cooldown model and weapon hooks.
    ///
    /// Implementations are MonoBehaviours on the hero prefab, discovered via
    /// GetComponent&lt;IBasicAttackBehaviour&gt;() in HeroController.Awake() — no Inspector
    /// dropdown or interface-serialization workaround; the correct script is whichever
    /// component is attached.
    /// </summary>
    public interface IBasicAttackBehaviour
    {
        /// <summary>
        /// SERVER-authoritative. Fires the attack. Bespoke implementations orchestrate
        /// multiple Delivery calls internally — HeroController doesn't know or care.
        /// </summary>
        void Fire(HeroController hero, Vector3 aimPoint);
    }

    /// <summary>
    /// Only implemented by behaviours that support press-and-hold (Kerem,
    /// AP-in-Ultimate). HeroController checks `basicAttack as IHoldableBasicAttack` —
    /// no empty no-op methods forced onto the 6 simple heroes.
    /// </summary>
    public interface IHoldableBasicAttack
    {
        /// <summary>
        /// Is the hold branch live RIGHT NOW? Kerem: always. AP: only while
        /// IsInUltimateMode (GS-17 §6.4 — the mode switch, not timing, decides).
        /// When false, HeroController treats the behaviour as tap-only this frame.
        /// </summary>
        bool HoldEnabled(HeroController hero);

        /// <summary>OWNER-side. Called once when the press crosses the hold threshold.</summary>
        void OnHoldBegin(HeroController hero, Vector3 aimPoint);

        /// <summary>OWNER-side. Called every frame while held (current mouse world point).</summary>
        void OnHoldUpdate(HeroController hero, Vector3 currentWorldPoint);

        /// <summary>OWNER-side. Called on release of a hold that began.</summary>
        void OnHoldRelease(HeroController hero);
    }
}
