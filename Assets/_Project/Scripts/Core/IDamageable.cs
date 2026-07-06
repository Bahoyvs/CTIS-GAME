using UnityEngine;

namespace CBuilding.Core
{
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

        public DamageInfo(float amount, Vector3 hitPoint, Vector3 knockbackDirection,
                          float knockbackForce, GameObject instigator)
        {
            Amount = amount;
            HitPoint = hitPoint;
            KnockbackDirection = knockbackDirection.sqrMagnitude > 0.0001f
                ? knockbackDirection.normalized
                : Vector3.zero;
            KnockbackForce = knockbackForce;
            Instigator = instigator;
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
