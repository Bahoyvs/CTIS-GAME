using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Abilities
{
    /// <summary>
    /// GS-9 — the shared server-side driver for <see cref="IPassiveTrigger"/> instances.
    /// Sits next to AbilityController on any hero that has non-cast passives (Bahadır's
    /// Passive + Final Passive today). Hero-specific installers (e.g. BahadirPassiveInstaller)
    /// construct the concrete IPassiveTrigger objects and Register() them here; this class
    /// itself contains zero hero-specific logic, matching GS-9.3's "no hero branching" rule.
    /// </summary>
    public class PassiveController : NetworkBehaviour
    {
        private readonly List<IPassiveTrigger> _triggers = new();

        public void Register(IPassiveTrigger trigger)
        {
            if (trigger == null || _triggers.Contains(trigger)) return;
            _triggers.Add(trigger);
        }

        public void Unregister(IPassiveTrigger trigger)
        {
            if (trigger == null) return;
            trigger.Shutdown();
            _triggers.Remove(trigger);
        }

        public override void OnNetworkDespawn()
        {
            for (int i = 0; i < _triggers.Count; i++) _triggers[i].Shutdown();
            _triggers.Clear();
        }

        private void Update()
        {
            if (!IsServer || _triggers.Count == 0) return;

            float dt = Time.deltaTime;
            for (int i = 0; i < _triggers.Count; i++)
            {
                _triggers[i].ServerTick(dt);
            }
        }
    }
}
