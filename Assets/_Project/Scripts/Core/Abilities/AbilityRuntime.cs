using UnityEngine;

namespace CBuilding.Abilities
{
    /// <summary>
    /// GS-9.1 — logic half of the config/logic split. Instantiated per hero per slot
    /// by AbilityController. ALL hooks run on the SERVER; cosmetic feedback reaches
    /// clients via AbilityController's activation ClientRpc, never from here.
    /// </summary>
    public abstract class AbilityRuntime
    {
        public AbilityDataSO Data { get; private set; }
        public AbilityController Controller { get; private set; }
        public AbilitySlot Slot { get; private set; }

        public void Initialize(AbilityController controller, AbilityDataSO data, AbilitySlot slot)
        {
            Controller = controller;
            Data = data;
            Slot = slot;
            OnInitialize();
        }

        /// <summary>One-time server-side setup (subscribe to events, cache components).</summary>
        protected virtual void OnInitialize() { }

        /// <summary>Extra server-side gating beyond cooldown/silence (range, resource, state).</summary>
        public virtual bool CanActivate() => true;

        /// <summary>Server: the ability fires (or toggles ON / channel starts).</summary>
        public abstract void Execute();

        /// <summary>Server: per-frame while channeling (mode = Channel).</summary>
        public virtual void ChannelTick(float deltaTime) { }

        /// <summary>Server: channel completed or was ended early.</summary>
        public virtual void ChannelEnd(bool completed) { }

        /// <summary>Server: toggle switched OFF (mode = Toggle).</summary>
        public virtual void ToggleEnd() { }

        /// <summary>
        /// Server: ask the controller to end an active channel before its full duration.
        /// <paramref name="cooldownRefund"/> supports AP's 'early landing refunds CD'.
        /// </summary>
        protected void EndChannelEarly(float cooldownRefund = 0f)
        {
            Controller.ServerEndChannel(Slot, completed: true, cooldownRefund);
        }
    }
}
