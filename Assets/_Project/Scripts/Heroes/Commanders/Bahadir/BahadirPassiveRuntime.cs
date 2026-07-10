using CBuilding.Abilities;
using CBuilding.Abilities.Delivery;
using CBuilding.Core;
using CBuilding.StatusEffects;
using UnityEngine;

namespace CBuilding.Heroes.Bahadir
{
    /// <summary>
    /// Passive: a speed pulse to nearby allies (TeamFilter = AlliesAndSelf), proc'd two ways
    /// — a throttled proximity check, AND whenever one of Bahadır's own Spyware marks kills
    /// its target (the "virus-dönüşü" — virus-return — proc). Not an AbilityDataSO: the
    /// player never presses a button for this (GS-9 §4), so it lives outside the
    /// AbilityController pipeline entirely and is driven by <see cref="PassiveController"/>.
    /// </summary>
    public class BahadirPassiveRuntime : IPassiveTrigger
    {
        private readonly ComposedAbilitySO _buffAbility;
        private readonly float _proximityCheckInterval;
        private readonly float _proximityRange;
        private readonly float _procCooldown;

        private AbilityController _controller;
        private float _checkTimer;
        private float _procCooldownTimer;

        public BahadirPassiveRuntime(ComposedAbilitySO buffAbility, float proximityCheckInterval,
            float proximityRange, float procCooldown)
        {
            _buffAbility = buffAbility;
            _proximityCheckInterval = proximityCheckInterval;
            _proximityRange = proximityRange;
            _procCooldown = procCooldown;
        }

        public void Initialize(AbilityController controller)
        {
            _controller = controller;
            _checkTimer = _proximityCheckInterval;
            SpywareMarkStatus.OnMarkedTargetDied += HandleMarkedTargetDied;
        }

        public void ServerTick(float deltaTime)
        {
            if (_procCooldownTimer > 0f) _procCooldownTimer -= deltaTime;

            _checkTimer -= deltaTime;
            if (_checkTimer > 0f) return;
            _checkTimer = _proximityCheckInterval;

            if (HasNearbyAlly()) TryProc();
        }

        public void Shutdown()
        {
            SpywareMarkStatus.OnMarkedTargetDied -= HandleMarkedTargetDied;
        }

        private bool HasNearbyAlly()
        {
            foreach (var ally in TeamRoster.GetAllHeroes())
            {
                if (ally.gameObject == _controller.gameObject) continue;
                float dist = Vector3.Distance(ally.transform.position, _controller.transform.position);
                if (dist <= _proximityRange) return true;
            }
            return false;
        }

        private void HandleMarkedTargetDied(GameObject source, GameObject victim)
        {
            if (source != _controller.gameObject) return;
            TryProc();
        }

        private void TryProc()
        {
            if (_procCooldownTimer > 0f || _buffAbility == null) return;
            _procCooldownTimer = _procCooldown;
            _buffAbility.ExecuteDelivery(_controller, _controller.transform.position);
        }
    }
}
