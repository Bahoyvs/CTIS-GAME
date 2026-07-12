using UnityEngine;
using CBuilding.Core;
using CBuilding.Heroes;
using CBuilding.StatusEffects;

namespace CBuilding.Enemies
{
    /// <summary>
    /// Frontal cone special, optionally alternating with default melee:
    ///   Wyrmling     — fire cone every 6s, bites between breaths
    ///   Sweeper-Claw — heavy sweep + stun every 6s OR on target switch
    ///   Bile-Vomiter — vomit cone every 5s, leaves a corrosive puddle
    /// The cone tests BaseHero.ActiveHeroes against angle+range from the owner, facing the
    /// current target — no physics setup. Fired from RosterEnemy.TickAttack (server-only).
    /// </summary>
    [RequireComponent(typeof(RosterEnemy))]
    public class EnemyConeAttack : EnemyAttackBehaviour
    {
        [Header("Cone")]
        [Range(10f, 180f)] [SerializeField] private float coneAngle = 70f;
        [Min(0.5f)] [SerializeField] private float coneRange = 4f;
        [Min(0f)] [SerializeField] private float coneDamage = 15f;
        [Min(0f)] [SerializeField] private float knockbackForce = 3f;

        [Tooltip("Applied to every hero caught in the cone (Sweeper-Claw stun).")]
        [SerializeField] private EffectDataSO appliedEffect;

        [Header("Aftermath")]
        [Tooltip("Hazard puddle dropped in front (Bile-Vomiter's 5s corrosive pool).")]
        [SerializeField] private EnemyHazardZone puddlePrefab;
        [Min(0f)] [SerializeField] private float puddleDistance = 2f;

        [Header("Cadence")]
        [Tooltip("Seconds between cone specials. 0 = EVERY attack is a cone.")]
        [Min(0f)] [SerializeField] private float specialInterval = 6f;

        [Tooltip("Use default melee while the special recharges (off = wait doing nothing).")]
        [SerializeField] private bool meleeBetweenSpecials = true;

        [Tooltip("Sweeper-Claw: switching targets also arms the next attack as a cone.")]
        [SerializeField] private bool armOnTargetSwitch;

        private RosterEnemy _owner;
        private float _nextSpecialTime;
        private bool _armed;

        private void Awake() => _owner = GetComponent<RosterEnemy>();

        private void OnEnable()
        {
            _nextSpecialTime = 0f; // Pool-safe: fresh life, special ready.
            _armed = false;
            if (armOnTargetSwitch) _owner.OnTargetSwitched += HandleTargetSwitched;
        }

        private void OnDisable()
        {
            if (armOnTargetSwitch) _owner.OnTargetSwitched -= HandleTargetSwitched;
        }

        private void HandleTargetSwitched(BaseHero previous, BaseHero next)
        {
            if (next != null && previous != null) _armed = true; // First acquisition doesn't arm.
        }

        public override void ExecuteAttack(RosterEnemy owner, BaseHero target)
        {
            bool special = _armed || specialInterval <= 0f || Time.time >= _nextSpecialTime;
            if (!special)
            {
                if (meleeBetweenSpecials) owner.PerformDefaultMelee(target);
                return;
            }

            _armed = false;
            _nextSpecialTime = Time.time + specialInterval;

            Vector3 forward = target.transform.position - owner.transform.position;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) return;
            forward.Normalize();

            int victims = 0;
            foreach (BaseHero hero in BaseHero.ActiveHeroes)
            {
                if (hero == null || !hero.IsAlive) continue;

                Vector3 to = hero.transform.position - owner.transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > coneRange * coneRange) continue;
                if (Vector3.Angle(forward, to) > coneAngle * 0.5f) continue;

                hero.TakeDamage(new DamageInfo(
                    coneDamage, hero.transform.position, to, knockbackForce,
                    owner.gameObject, DamageFlags.Melee));

                if (appliedEffect != null && hero.TryGetComponent(out StatusEffectController status))
                    status.ApplyEffect(appliedEffect, owner.gameObject);
                victims++;
            }

            if (puddlePrefab != null)
            {
                Vector3 pos = owner.transform.position + forward * puddleDistance;
                EnemyHazardZone puddle = Instantiate(puddlePrefab, pos, Quaternion.identity);
                puddle.ServerInit(owner.gameObject);
                puddle.NetworkObject.Spawn(true);
            }

            CombatLogManager.LogAction(owner.DisplayName, "used",
                $"Cone_Attack hitting {victims} hero(es)", owner.transform.position);
        }
    }
}
