using System.Collections;
using CBuilding.Abilities;
using CBuilding.Abilities.Delivery;
using CBuilding.StatusEffects;
using UnityEngine;

namespace CBuilding.Heroes.Bahadir
{
    /// <summary>Logic half of <see cref="BahadirSkill2DataSO"/>. See that class for the design note.</summary>
    public class BahadirSkill2Runtime : AbilityRuntime
    {
        private bool _returningVirus;
        private Coroutine _windowRoutine;

        protected override void OnInitialize()
        {
            SpywareMarkStatus.OnMarkedTargetDied += HandleMarkedTargetDied;
            ApplyStatusEffectSO.OnAnyStatusApplied += HandleAnyStatusApplied;
        }

        public override void Execute()
        {
            var data = (BahadirSkill2DataSO)Data;
            data.markAbility?.ExecuteDelivery(Controller, Controller.CurrentAimPoint);
        }

        // ---- Virus-return: a target WE marked died while marked ----

        private void HandleMarkedTargetDied(GameObject source, GameObject victim)
        {
            if (source != Controller.gameObject) return;
            OnVirusReturned();
        }

        private void OnVirusReturned()
        {
            var data = (BahadirSkill2DataSO)Data;

            if (Controller.TryGetComponent<StatusEffectController>(out var status) && data.cooldownReductionEffect != null)
            {
                status.ApplyEffect(data.cooldownReductionEffect, Controller.gameObject);
            }

            _returningVirus = true;
            if (_windowRoutine != null) Controller.StopCoroutine(_windowRoutine);
            _windowRoutine = Controller.StartCoroutine(ChainWindowCoroutine(data.chainWindowSeconds));
        }

        private IEnumerator ChainWindowCoroutine(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            _returningVirus = false;
            _windowRoutine = null;
        }

        // ---- Chain re-mark: the next Bahadır-sourced stun inside the window ----

        private void HandleAnyStatusApplied(EffectDataSO appliedEffect, GameObject caster, GameObject target)
        {
            var data = (BahadirSkill2DataSO)Data;
            if (!_returningVirus) return;
            if (caster != Controller.gameObject) return;
            if (data.bahadirStunEffect == null || appliedEffect != data.bahadirStunEffect) return;

            OnBahadirSourcedStunLanded(target, data);
        }

        private void OnBahadirSourcedStunLanded(GameObject target, BahadirSkill2DataSO data)
        {
            _returningVirus = false;
            if (_windowRoutine != null)
            {
                Controller.StopCoroutine(_windowRoutine);
                _windowRoutine = null;
            }

            if (data.chainMarkEffect == null) return;

            var ctx = new EffectContext(target, Controller.gameObject, target.transform.position, Controller.transform.position);
            data.chainMarkEffect.Apply(in ctx); // no charge spent — the mark shot itself wasn't fired
        }
    }
}
