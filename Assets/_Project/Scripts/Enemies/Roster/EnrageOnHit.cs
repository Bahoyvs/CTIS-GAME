using UnityEngine;
using CBuilding.Core;

namespace CBuilding.Enemies.Roster
{
    /// <summary>
    /// Big Bertha: slow baseline swings, but the first hit taken flips her into a
    /// +100% attack-speed frenzy for 10 seconds. Not re-triggerable while active;
    /// the next hit AFTER the frenzy ends starts a new one. Reactive-tank pressure:
    /// leaving her alone is safe, poking her is not.
    /// Server-only by construction — BaseEnemy.OnDamaged never fires on clients.
    /// </summary>
    [RequireComponent(typeof(RosterEnemy))]
    public class EnrageOnHit : MonoBehaviour
    {
        [Min(1f)] [SerializeField] private float attackSpeedMultiplier = 2f;
        [Min(0.5f)] [SerializeField] private float enrageDuration = 10f;

        private RosterEnemy _enemy;
        private float _enrageEndTime;

        private void Awake() => _enemy = GetComponent<RosterEnemy>();

        private void OnEnable()
        {
            _enrageEndTime = 0f; // Pool-safe: calm at the start of every life.
            _enemy.OnDamaged += HandleDamaged;
        }

        private void OnDisable() => _enemy.OnDamaged -= HandleDamaged;

        private void HandleDamaged(DamageInfo info)
        {
            if (info.IsHealing || (info.Flags & DamageFlags.DoT) != 0) return; // Ticks don't wake her.
            if (Time.time < _enrageEndTime) return;                            // Already furious.

            _enrageEndTime = Time.time + enrageDuration;
            _enemy.AddAttackSpeedMultiplier(this, attackSpeedMultiplier, enrageDuration);
            CombatLogManager.LogAction(_enemy.DisplayName, "entered", "Enrage", transform.position);
        }
    }
}
