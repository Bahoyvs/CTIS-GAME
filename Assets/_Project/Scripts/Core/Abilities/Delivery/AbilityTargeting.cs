using CBuilding.Core;
using CBuilding.Heroes;
using UnityEngine;

namespace CBuilding.Abilities.Delivery
{
    /// <summary>
    /// Who a delivery is allowed to affect, relative to the caster.
    /// Examples: Kerem S1 = Enemies. Gobluna S1 = EnemiesAndAllies (damage one, heal the
    /// other — each AbilityEffectSO filters again per-target). Ok S2 = Allies.
    /// </summary>
    public enum TeamFilter : byte
    {
        Enemies,
        Allies,          // heroes other than the caster
        AlliesAndSelf,
        EnemiesAndAllies, // everyone except the caster
        All               // everyone including the caster
    }

    /// <summary>Server-side cast snapshot handed to deliveries.</summary>
    public readonly struct AbilityCastContext
    {
        public readonly AbilityController Controller;
        public readonly ComposedAbilitySO Ability;
        public readonly Vector3 AimPoint; // raw owner aim; deliveries clamp to their own range

        public GameObject Caster => Controller.gameObject;
        public Vector3 Origin => Controller.transform.position;

        public AbilityCastContext(AbilityController controller, ComposedAbilitySO ability, Vector3 aimPoint)
        {
            Controller = controller;
            Ability = ability;
            AimPoint = aimPoint;
        }
    }

    /// <summary>Server-side per-target snapshot handed to effects.</summary>
    public readonly struct EffectContext
    {
        public readonly GameObject Target;   // damageable root
        public readonly GameObject Caster;
        public readonly Vector3 HitPoint;
        public readonly Vector3 CastOrigin;  // for displacement direction (push away / pull toward)

        public EffectContext(GameObject target, GameObject caster, Vector3 hitPoint, Vector3 castOrigin)
        {
            Target = target;
            Caster = caster;
            HitPoint = hitPoint;
            CastOrigin = castOrigin;
        }

        public bool TargetIsHero => Target.GetComponent<BaseHero>() != null;
    }

    /// <summary>Shared server-side helpers for deliveries, zones and projectiles.</summary>
    public static class AbilityTargeting
    {
        /// <summary>Collider → living damageable root, or null.</summary>
        public static GameObject ResolveRoot(Collider col)
        {
            var damageable = col.GetComponentInParent<IDamageable>();
            if (damageable == null || !damageable.IsAlive) return null;
            return ((Component)damageable).gameObject;
        }

        /// <summary>Team check relative to the caster. Heroes = BaseHero; everything else = enemy side.</summary>
        public static bool PassesFilter(GameObject root, GameObject caster, TeamFilter filter)
        {
            if (root == null) return false;

            bool isSelf = root == caster;
            bool isHero = root.GetComponent<BaseHero>() != null;

            return filter switch
            {
                TeamFilter.Enemies => !isHero && !isSelf,
                TeamFilter.Allies => isHero && !isSelf,
                TeamFilter.AlliesAndSelf => isHero,
                TeamFilter.EnemiesAndAllies => !isSelf,
                TeamFilter.All => true,
                _ => false
            };
        }

        /// <summary>Runs the ability's whole effect list against one target.</summary>
        public static void ApplyEffects(ComposedAbilitySO ability, GameObject targetRoot,
            GameObject caster, Vector3 hitPoint, Vector3 castOrigin)
        {
            if (targetRoot == null || ability.effects == null) return;

            var ctx = new EffectContext(targetRoot, caster, hitPoint, castOrigin);
            for (int i = 0; i < ability.effects.Length; i++)
            {
                if (ability.effects[i] != null) ability.effects[i].Apply(in ctx);
            }
        }
    }
}
