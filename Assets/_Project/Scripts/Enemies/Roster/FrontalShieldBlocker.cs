using UnityEngine;
using CBuilding.Core;

namespace CBuilding.Enemies.Roster
{
    /// <summary>
    /// Vanguard Vanguardian: a frontal shield that reduces damage arriving from the front
    /// arc by 50% until its pool is depleted — then it breaks PERMANENTLY (this life).
    /// Teaches flanking: rear/side hits always land in full.
    ///
    /// "Front" = the direction the enemy is engaging (toward its current target); with no
    /// target the shield covers the attacker's direction (guard-up idle). IDamageModifier
    /// at priority 150 (multiplicative band). Pool-safe: re-arms in OnEnable each life.
    /// </summary>
    [RequireComponent(typeof(RosterEnemy))]
    public class FrontalShieldBlocker : MonoBehaviour, IDamageModifier
    {
        [Tooltip("Total damage the shield can absorb before breaking (roster: 500).")]
        [Min(1f)] [SerializeField] private float shieldHealth = 500f;

        [Tooltip("Fraction of frontal damage blocked while intact (roster: 50%).")]
        [Range(0f, 1f)] [SerializeField] private float frontalReduction = 0.5f;

        [Tooltip("Total frontal arc in degrees.")]
        [Range(10f, 360f)] [SerializeField] private float frontalArc = 150f;

        public int Priority => 150;

        public bool IsBroken => _broken;

        private RosterEnemy _enemy;
        private DamageModifierPipeline _pipeline;
        private float _remaining;
        private bool _broken;

        private void Awake()
        {
            _enemy = GetComponent<RosterEnemy>();
            _pipeline = GetComponent<DamageModifierPipeline>();
        }

        private void OnEnable()
        {
            _remaining = shieldHealth; // Pool-safe: fresh shield every life.
            _broken = false;
            _pipeline?.Register(this);
        }

        private void OnDisable() => _pipeline?.Unregister(this);

        public float Modify(in DamageInfo info, float currentAmount)
        {
            if (_broken || info.IsHealing || currentAmount <= 0f || info.Instigator == null)
                return currentAmount;

            Vector3 toAttacker = info.Instigator.transform.position - transform.position;
            toAttacker.y = 0f;

            Vector3 facing = _enemy.Target != null
                ? _enemy.Target.transform.position - transform.position
                : toAttacker; // No engagement: guard up toward whoever shoots.
            facing.y = 0f;

            if (Vector3.Angle(facing, toAttacker) > frontalArc * 0.5f)
                return currentAmount; // Flank hit — full damage.

            float blocked = currentAmount * frontalReduction;
            _remaining -= blocked;
            if (_remaining <= 0f) _broken = true; // Pops for good.

            return currentAmount - blocked;
        }
    }
}
