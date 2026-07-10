using System.Collections.Generic;
using UnityEngine;

namespace CBuilding.Core
{
    /// <summary>
    /// GS-5.4 — per-entity ordered chain of <see cref="IDamageModifier"/>s.
    /// BaseHero and BaseEnemy route every TakeDamage/ServerHeal amount through this
    /// (when present) BEFORE armor/health math. Server-side only: modifiers are
    /// registered by server-authoritative systems (status effects, items, synergies).
    /// </summary>
    public class DamageModifierPipeline : MonoBehaviour
    {
        private readonly List<IDamageModifier> _modifiers = new();

        public void Register(IDamageModifier modifier)
        {
            if (modifier == null || _modifiers.Contains(modifier)) return;
            _modifiers.Add(modifier);
            _modifiers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        public void Unregister(IDamageModifier modifier)
        {
            _modifiers.Remove(modifier);
        }

        /// <summary>Runs the chain and returns the final amount (never negative).</summary>
        public float Process(in DamageInfo info)
        {
            float amount = info.Amount;

            if ((info.Flags & DamageFlags.BypassModifiers) == 0)
            {
                for (int i = 0; i < _modifiers.Count; i++)
                {
                    amount = _modifiers[i].Modify(in info, amount);
                }
            }

            return Mathf.Max(0f, amount);
        }
    }
}
