using CBuilding.Abilities;
using CBuilding.Abilities.Delivery;
using CBuilding.StatusEffects;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Heroes.Bahadir
{
    /// <summary>
    /// Prefab wiring for Bahadır's two non-cast passives (GS-9 §4: they are
    /// <see cref="IPassiveTrigger"/>, not AbilityDataSO, so nothing in AbilityController's
    /// slot system constructs them). Add this next to AbilityController + PassiveController
    /// on the Bahadır hero prefab and assign the fields below.
    /// </summary>
    [RequireComponent(typeof(AbilityController))]
    [RequireComponent(typeof(PassiveController))]
    public class BahadirPassiveInstaller : NetworkBehaviour
    {
        [Header("Passive — proximity / virus-return buff")]
        [Tooltip("CA_Bahadir_Passive_Buff — PointArea, SpeedBuff, TeamFilter = AlliesAndSelf.")]
        public ComposedAbilitySO passiveBuffAbility;
        [Min(0.05f)] public float proximityCheckInterval = 0.5f;
        [Min(0.5f)] public float proximityRange = 5f;
        [Min(0f)] public float procCooldown = 4f;

        [Header("Final Passive — team-wide kill reaction")]
        [Tooltip("Fx_CooldownReduction — instant status.")]
        public EffectDataSO finalPassiveCooldownReduction;
        [Tooltip("Fx_SpeedBuff — data-driven moveSpeedMultiplier status.")]
        public EffectDataSO finalPassiveSpeedBuff;
        [Min(0f)] public float finalPassiveDebounceSeconds = 0.5f;

        private PassiveController _passives;
        private BahadirPassiveRuntime _passiveRuntime;
        private BahadirFinalPassiveRuntime _finalPassiveRuntime;

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            var abilities = GetComponent<AbilityController>();
            _passives = GetComponent<PassiveController>();

            _passiveRuntime = new BahadirPassiveRuntime(
                passiveBuffAbility, proximityCheckInterval, proximityRange, procCooldown);
            _passiveRuntime.Initialize(abilities);
            _passives.Register(_passiveRuntime);

            _finalPassiveRuntime = new BahadirFinalPassiveRuntime(
                finalPassiveCooldownReduction, finalPassiveSpeedBuff, finalPassiveDebounceSeconds);
            _finalPassiveRuntime.Initialize(abilities);
            _passives.Register(_finalPassiveRuntime);
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer || _passives == null) return;

            if (_passiveRuntime != null) _passives.Unregister(_passiveRuntime);
            if (_finalPassiveRuntime != null) _passives.Unregister(_finalPassiveRuntime);
        }
    }
}
