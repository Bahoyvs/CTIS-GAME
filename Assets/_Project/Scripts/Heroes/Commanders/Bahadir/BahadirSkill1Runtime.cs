using CBuilding.Abilities;
using CBuilding.Abilities.Delivery;
using CBuilding.Enemies;
using CBuilding.StatusEffects;

namespace CBuilding.Heroes.Bahadir
{
    /// <summary>Logic half of <see cref="BahadirSkill1DataSO"/>. See that class for the design note.</summary>
    public class BahadirSkill1Runtime : AbilityRuntime
    {
        private int _currentForm; // 0 = Form0 ("0"), 1 = Form1 ("1")

        /// <summary>
        /// Fires the current form, then flips to the other one for next time — no separate
        /// toggle input, no timing window, no mount-ride. Simplest possible two-form switch.
        /// </summary>
        public override void Execute()
        {
            FireCurrentForm((BahadirSkill1DataSO)Data);
            _currentForm = 1 - _currentForm;
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
