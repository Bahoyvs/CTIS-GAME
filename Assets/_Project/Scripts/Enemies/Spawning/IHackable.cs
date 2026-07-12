using CBuilding.StatusEffects;
using UnityEngine;

namespace CBuilding.Enemies.Spawning
{
    /// <summary>
    /// Anything Bahadır's Ultimate (or future kits) can hack. SpawnNode implements it so
    /// the ability layer never needs to know what a "node" is — it just raycasts/overlaps
    /// for IHackable colliders and calls ServerHack.
    /// </summary>
    public interface IHackable
    {
        /// <summary>False while already hacked or on an internal cooldown.</summary>
        bool CanBeHacked { get; }

        /// <summary>
        /// Server-only. Injects <paramref name="virusEffect"/> so it is applied to
        /// everything this object produces during the next <paramref name="duration"/> seconds.
        /// </summary>
        void ServerHack(GameObject hacker, EffectDataSO virusEffect, float duration);
    }
}
