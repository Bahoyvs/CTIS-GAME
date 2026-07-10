using System.Collections;
using CBuilding.Abilities;
using CBuilding.Abilities.Delivery;
using CBuilding.Enemies;
using CBuilding.StatusEffects;
using UnityEngine;

namespace CBuilding.Heroes.Bahadir
{
    /// <summary>Logic half of <see cref="BahadirUltimateDataSO"/>. See that class for the design note.</summary>
    public class BahadirUltimateRuntime : AbilityRuntime
    {
        private Vector3 _castOrigin;
        private Coroutine _windowRoutine;

        public override void Execute()
        {
            var data = (BahadirUltimateDataSO)Data;
            _castOrigin = Controller.transform.position;

            if (data.selfRootEffect != null && Controller.TryGetComponent<StatusEffectController>(out var status))
            {
                status.ApplyEffect(data.selfRootEffect, Controller.gameObject);
            }
        }

        public override void ChannelEnd(bool completed)
        {
            // Spawn-hack only opens on a clean finish — an interrupted channel didn't earn it.
            if (!completed) return;

            var data = (BahadirUltimateDataSO)Data;
            if (data.spawnHackMarkEffect == null) return;

            EnemySpawnHooks.OnEnemySpawned += HandleEnemySpawned;
            if (_windowRoutine != null) Controller.StopCoroutine(_windowRoutine);
            _windowRoutine = Controller.StartCoroutine(CloseWindowAfter(data.spawnHackWindowSeconds));
        }

        private void HandleEnemySpawned(BaseEnemy enemy)
        {
            var data = (BahadirUltimateDataSO)Data;
            if (Vector3.Distance(enemy.transform.position, _castOrigin) > data.infectionRadius) return;

            var ctx = new EffectContext(enemy.gameObject, Controller.gameObject, enemy.transform.position, _castOrigin);
            data.spawnHackMarkEffect.Apply(in ctx);
        }

        private IEnumerator CloseWindowAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            EnemySpawnHooks.OnEnemySpawned -= HandleEnemySpawned;
            _windowRoutine = null;
        }
    }
}
