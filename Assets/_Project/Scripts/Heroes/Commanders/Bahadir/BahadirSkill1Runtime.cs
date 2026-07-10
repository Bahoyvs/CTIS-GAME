using CBuilding.Abilities;
using CBuilding.Abilities.Delivery;
using CBuilding.Enemies;
using CBuilding.StatusEffects;
using UnityEngine;
// BaseHero lives in CBuilding.Heroes — this file's own namespace (CBuilding.Heroes.Bahadir)
// is a different namespace, so it still needs an explicit using.
using CBuilding.Heroes;

namespace CBuilding.Heroes.Bahadir
{
    /// <summary>Logic half of <see cref="BahadirSkill1DataSO"/>. See that class for the design note.</summary>
    public class BahadirSkill1Runtime : AbilityRuntime
    {
        private int _currentForm; // 0 = Form0 ("0"), 1 = Form1 ("1")
        private float _lastPressTime = -999f;

        public override void Execute()
        {
            var data = (BahadirSkill1DataSO)Data;
            float now = Time.time;
            bool isDoubleTap = (now - _lastPressTime) <= data.doubleTapWindow;
            _lastPressTime = now;

            if (isDoubleTap)
            {
                ToggleForm();
                TriggerMountRide(data);
                return;
            }

            FireCurrentForm(data);
        }

        private void ToggleForm() => _currentForm = 1 - _currentForm;

        private void TriggerMountRide(BahadirSkill1DataSO data)
        {
            // Bespoke mount-ride movement is out of this doc's scope (GS-9.4, prior doc).
            // Placeholder: a brief speed burst so the toggle has SOME immediate feel until
            // the real bespoke state machine lands.
            if (Controller.TryGetComponent<BaseHero>(out var hero))
            {
                hero.ServerApplySpeedBuff(data.mountRideSpeedMultiplier, data.mountRideDuration);
            }
        }

        private void FireCurrentForm(BahadirSkill1DataSO data)
        {
            if (_currentForm == 0)
            {
                data.form0Ability?.ExecuteDelivery(Controller, Controller.CurrentAimPoint);
                return;
            }

            data.form1Ability?.ExecuteDelivery(Controller, Controller.CurrentAimPoint);
            ApplySpywareChainBonus(data);
        }

        /// <summary>
        /// GS-9 §1: "the delivery layer is only needed while acquiring targets — if the
        /// targets are already known (registry query), apply Effect SO's directly."
        /// </summary>
        private void ApplySpywareChainBonus(BahadirSkill1DataSO data)
        {
            if (data.chainDamageEffect == null && data.chainSlowEffect == null) return;

            foreach (BaseEnemy marked in EnemyRegistry.GetAllWithEffect<SpywareMarkStatus>())
            {
                var ctx = new EffectContext(
                    marked.gameObject, Controller.gameObject,
                    marked.transform.position, Controller.transform.position);

                data.chainDamageEffect?.Apply(in ctx);
                data.chainSlowEffect?.Apply(in ctx);
            }
        }
    }
}
