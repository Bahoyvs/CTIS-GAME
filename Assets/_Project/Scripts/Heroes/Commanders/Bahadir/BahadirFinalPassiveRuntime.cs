using CBuilding.Abilities;
using CBuilding.Core;
using CBuilding.Enemies;
using CBuilding.StatusEffects;
using UnityEngine;

namespace CBuilding.Heroes.Bahadir
{
    /// <summary>
    /// Final Passive: whenever any ally lands a kill, the WHOLE roster gets a CD pulse +
    /// speed buff. TeamEventBus.OnAllyKilledEnemy is roster-wide already, so this doesn't
    /// need a spatial Delivery at all (GS-9 §2 architecture note) — it walks TeamRoster and
    /// applies both Effect SO's directly. Debounced so simultaneous kills (AoE wipes) don't
    /// stack the buff N times in one frame.
    /// </summary>
    public class BahadirFinalPassiveRuntime : IPassiveTrigger
    {
        private readonly EffectDataSO _cooldownReductionEffect;
        private readonly EffectDataSO _speedBuffEffect;
        private readonly float _debounceSeconds;

        private AbilityController _controller;
        private float _debounceTimer;

        public BahadirFinalPassiveRuntime(EffectDataSO cooldownReductionEffect, EffectDataSO speedBuffEffect, float debounceSeconds)
        {
            _cooldownReductionEffect = cooldownReductionEffect;
            _speedBuffEffect = speedBuffEffect;
            _debounceSeconds = debounceSeconds;
        }

        public void Initialize(AbilityController controller)
        {
            _controller = controller;
            TeamEventBus.OnAllyKilledEnemy += HandleAllyKilledEnemy;
        }

        public void ServerTick(float deltaTime)
        {
            if (_debounceTimer > 0f) _debounceTimer -= deltaTime;
        }

        public void Shutdown()
        {
            TeamEventBus.OnAllyKilledEnemy -= HandleAllyKilledEnemy;
        }

        private void HandleAllyKilledEnemy(GameObject ally, BaseEnemy enemy)
        {
            if (_debounceTimer > 0f) return;
            _debounceTimer = _debounceSeconds;

            foreach (var hero in TeamRoster.GetAllHeroes())
            {
                if (!hero.TryGetComponent<StatusEffectController>(out var status)) continue;
                if (_cooldownReductionEffect != null) status.ApplyEffect(_cooldownReductionEffect, hero.gameObject);
                if (_speedBuffEffect != null) status.ApplyEffect(_speedBuffEffect, hero.gameObject);
            }
        }
    }
}
