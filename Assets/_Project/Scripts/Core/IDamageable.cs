using System;
using UnityEngine;

namespace CBuilding.Core
{
    /// <summary>
    /// GS-5.4 — classification flags carried by every hit/heal so IDamageModifier
    /// implementations (anti-heal, SpywareMark, Mark of Guilt, Sunburn...) can key
    /// off them without special-case code at damage call sites.
    /// </summary>
    [Flags]
    public enum DamageFlags
    {
        None = 0,
        /// <summary>Positive amount restores HP. Anti-heal modifiers key off this flag.</summary>
        Healing = 1 << 0,
        /// <summary>Damage-over-time tick from a status effect (GS-5). Usually no knockback/hitstun.</summary>
        DoT = 1 << 1,
        /// <summary>Environmental hazard source (GS-7).</summary>
        Hazard = 1 << 2,
        /// <summary>Ability-sourced (GS-9).</summary>
        Ability = 1 << 3,
        /// <summary>Skips the IDamageModifier chain (reserved for scripted kills; use sparingly).</summary>
        BypassModifiers = 1 << 4,
        /// <summary>Melee/contact damage (BaseEnemy.TickAttack). Bahadır's Feature ghost keys off this.</summary>
        Melee = 1 << 5,
    }

    /// <summary>
    /// Payload describing a single hit. Passed by 'in' reference (readonly struct)
    /// to avoid GC allocations in hot combat paths.
    /// </summary>
    public readonly struct DamageInfo
    {
        public readonly float Amount;
        public readonly Vector3 HitPoint;            // World-space impact point (VFX spawn, etc.)
        public readonly Vector3 KnockbackDirection;  // Normalized, XZ plane
        public readonly float KnockbackForce;
        public readonly GameObject Instigator;       // Who dealt the damage
        public readonly DamageFlags Flags;           // GS-5.4 classification

        public bool IsHealing => (Flags & DamageFlags.Healing) != 0;

        public DamageInfo(float amount, Vector3 hitPoint, Vector3 knockbackDirection,
                          float knockbackForce, GameObject instigator,
                          DamageFlags flags = DamageFlags.None)
        {
            Amount = amount;
            HitPoint = hitPoint;
            KnockbackDirection = knockbackDirection.sqrMagnitude > 0.0001f
                ? knockbackDirection.normalized
                : Vector3.zero;
            KnockbackForce = knockbackForce;
            Instigator = instigator;
            Flags = flags;
        }
    }

    /// <summary>
    /// Contract for anything that can take damage: heroes, enemies, destructible props.
    /// Attackers only ever talk to this interface — they never need to know if the target
    /// is a BaseEnemy, BaseHero, or a breakable vending machine in the CTIS lobby.
    /// </summary>
    public interface IDamageable
    {
        bool IsAlive { get; }
        void TakeDamage(in DamageInfo info);
        void Die();
    }
}
