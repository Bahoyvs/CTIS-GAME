using CBuilding.Core;
using UnityEngine;

namespace CBuilding.Abilities.Delivery
{
    /// <summary>Instant damage through the GS-5.4 pipeline. Kerem S1 explosion, TL thorns, AP laser ticks.</summary>
    [CreateAssetMenu(menuName = "CBuilding/Abilities/Effects/Damage", fileName = "Fx_Damage")]
    public class DamageEffectSO : AbilityEffectSO
    {
        [Min(0f)] public float damage = 10f;
        [Min(0f)] public float knockbackForce = 0f;

        protected override void OnApply(in EffectContext ctx)
        {
            if (!ctx.Target.TryGetComponent<IDamageable>(out var damageable)) return;

            Vector3 knockDir = ctx.Target.transform.position - ctx.CastOrigin;
            damageable.TakeDamage(new DamageInfo(
                damage, ctx.HitPoint, knockDir, knockbackForce, ctx.Caster, DamageFlags.Ability));
        }
    }
}
