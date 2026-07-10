using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Abilities.Delivery
{
    /// <summary>
    /// Generic persistent ability zone: applies its ability's effect list to valid
    /// targets inside the radius on every tick. Data comes from ZoneDeliverySO —
    /// Gobluna's green fire, Ironworks' Hex-Shield and Ug's wind tunnel are ASSETS
    /// of this, not new scripts. (Generalizes AreaOfEffectNetworked.)
    ///
    /// PREFAB: this + NetworkObject + child visual (ring sprite / particles).
    /// Register in the Network Prefabs list. Scale the visual to match the radius.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class AbilityZone : NetworkBehaviour
    {
        private static readonly Collider[] Buffer = new Collider[32];
        private readonly HashSet<GameObject> _affectedThisPulse = new();

        private ComposedAbilitySO _ability;
        private ZoneDeliverySO _settings;
        private GameObject _caster;
        private float _elapsed;
        private float _nextTickTime;

        /// <summary>Server-only. Call BEFORE NetworkObject.Spawn().</summary>
        public void ServerConfigure(ComposedAbilitySO ability, ZoneDeliverySO settings, GameObject caster)
        {
            _ability = ability;
            _settings = settings;
            _caster = caster;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) enabled = false;
        }

        private void Update()
        {
            if (_settings == null) return;

            _elapsed += Time.deltaTime;

            if (Time.time >= _nextTickTime)
            {
                _nextTickTime = Time.time + _settings.tickInterval;
                Pulse();
            }

            if (_elapsed >= _settings.duration)
            {
                if (NetworkObject.IsSpawned) NetworkObject.Despawn(true);
            }
        }

        private void Pulse()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, _settings.radius, Buffer,
                _settings.hitLayers, QueryTriggerInteraction.Collide);

            _affectedThisPulse.Clear();
            for (int i = 0; i < count; i++)
            {
                GameObject root = AbilityTargeting.ResolveRoot(Buffer[i]);
                if (root == null || !_affectedThisPulse.Add(root)) continue;
                if (!AbilityTargeting.PassesFilter(root, _caster, _ability.teamFilter)) continue;

                AbilityTargeting.ApplyEffects(
                    _ability, root, _caster, Buffer[i].ClosestPoint(transform.position), transform.position);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 1f, 0.4f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, _settings != null ? _settings.radius : 3f);
        }
#endif
    }
}
