using UnityEngine;
using CBuilding.Heroes;
using CBuilding.StatusEffects;

namespace CBuilding.Enemies.Roster
{
    /// <summary>
    /// Applies a status effect to heroes struck by this enemy's DEFAULT MELEE
    /// (The Leaper's 20% slow). Ranged/cone attacks carry their own effect fields;
    /// this covers the plain-melee path via RosterEnemy.OnMeleeHitLanded
    /// (server-only event by construction).
    /// </summary>
    [RequireComponent(typeof(RosterEnemy))]
    public class OnMeleeHitEffect : MonoBehaviour
    {
        [SerializeField] private EffectDataSO effect;

        private RosterEnemy _enemy;

        private void Awake() => _enemy = GetComponent<RosterEnemy>();
        private void OnEnable() => _enemy.OnMeleeHitLanded += HandleHit;
        private void OnDisable() => _enemy.OnMeleeHitLanded -= HandleHit;

        private void HandleHit(BaseHero victim)
        {
            if (effect == null || victim == null) return;
            if (victim.TryGetComponent(out StatusEffectController status))
                status.ApplyEffect(effect, gameObject);
        }
    }
}
