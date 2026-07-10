using CBuilding.Core;
using UnityEngine;

namespace CBuilding.Abilities.Delivery
{
    /// <summary>
    /// Push (away from cast origin) or Pull (toward it). Kerem S2's line pull,
    /// Kerem Ult explosion push, Ug's wind tunnel edges, Ironworks Ult push.
    /// Rides the existing knockback path (zero-damage DamageInfo), so enemy
    /// knockback-resistance stats keep working.
    /// </summary>
    [CreateAssetMenu(menuName = "CBuilding/Abilities/Effects/Displacement", fileName = "Fx_Displace")]
    public class DisplacementEffectSO : AbilityEffectSO
    {
        public enum Mode : byte { Push, Pull }

        public Mode mode = Mode.Push;
        [Min(0f)] public float force = 6f;

        protected override void OnApply(in EffectContext ctx)
        {
            if (!ctx.Target.TryGetComponent<IDamageable>(out var damageable)) return;

            Vector3 away = ctx.Target.transform.position - ctx.CastOrigin;
            away.y = 0f;
            Vector3 dir = mode == Mode.Push ? away : -away;

            damageable.TakeDamage(new DamageInfo(
                0f, ctx.HitPoint, dir, force, ctx.Caster, DamageFlags.Ability));
        }
    }
}
