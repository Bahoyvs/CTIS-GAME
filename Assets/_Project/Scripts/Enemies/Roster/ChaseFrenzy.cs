using UnityEngine;
using CBuilding.Heroes;

namespace CBuilding.Enemies.Roster
{
    /// <summary>
    /// Blood-Hound: whenever its aggro shifts to a NEW target it gains +60% move speed
    /// until it lands a melee hit — a terrier that sprints at whoever pulled it last.
    /// Pure event glue on RosterEnemy's OnTargetSwitched / OnMeleeHitLanded (server-only
    /// events by construction).
    /// </summary>
    [RequireComponent(typeof(RosterEnemy))]
    public class ChaseFrenzy : MonoBehaviour
    {
        [Min(1f)] [SerializeField] private float speedMultiplier = 1.6f;

        private RosterEnemy _enemy;

        private void Awake() => _enemy = GetComponent<RosterEnemy>();

        private void OnEnable()
        {
            _enemy.OnTargetSwitched += HandleTargetSwitched;
            _enemy.OnMeleeHitLanded += HandleHitLanded;
        }

        private void OnDisable()
        {
            _enemy.OnTargetSwitched -= HandleTargetSwitched;
            _enemy.OnMeleeHitLanded -= HandleHitLanded;
            _enemy.RemoveSpeedMultiplier(this);
        }

        private void HandleTargetSwitched(BaseHero previous, BaseHero next)
        {
            if (next != null) _enemy.AddSpeedMultiplier(this, speedMultiplier);
            else _enemy.RemoveSpeedMultiplier(this);
        }

        private void HandleHitLanded(BaseHero victim) => _enemy.RemoveSpeedMultiplier(this);
    }
}
