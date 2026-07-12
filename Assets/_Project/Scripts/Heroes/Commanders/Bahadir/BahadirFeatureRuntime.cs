using CBuilding.Abilities;
using UnityEngine;

namespace CBuilding.Heroes.Bahadir
{
    /// <summary>
    /// Logic half of <see cref="BahadirFeatureDataSO"/>. Casts the self-buff once, then
    /// re-fires the pass-through-stun delivery at Bahadır's current position on a throttled
    /// tick for the rest of the channel window. Both deliveries are 100% composable assets —
    /// this class exists only to drive the repeat-call, per the plan doc's coverage map.
    /// </summary>
    public class BahadirFeatureRuntime : AbilityRuntime
    {
        private float _tickTimer;

        public override void Execute()
        {
            var data = (BahadirFeatureDataSO)Data;
            data.buffAbility?.ExecuteDelivery(Controller, Controller.transform.position);

            // Fire one pulse immediately instead of waiting a full tick interval — matters
            // now that Roll (a ~0.25s dash) is what triggers this (BahadirRollController):
            // anything already inside the radius the instant Stealth turns on must not
            // have to wait for the first scheduled tick to get caught.
            data.passThroughStunAbility?.ExecuteDelivery(Controller, Controller.transform.position);
            _tickTimer = data.passThroughTickInterval;
        }

        public override void ChannelTick(float deltaTime)
        {
            var data = (BahadirFeatureDataSO)Data;
            if (data.passThroughStunAbility == null) return;

            _tickTimer -= deltaTime;
            while (_tickTimer <= 0f)
            {
                data.passThroughStunAbility.ExecuteDelivery(Controller, Controller.transform.position);
                _tickTimer += data.passThroughTickInterval;
            }
        }

        public override void ChannelEnd(bool completed)
        {
            _tickTimer = 0f;
        }
    }
}
