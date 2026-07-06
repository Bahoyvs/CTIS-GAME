using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using CBuilding.Core;
using CBuilding.Heroes;

namespace CBuilding.Combat
{
    /// <summary>
    /// Server-authoritative synergy zone (Module 3): a Guardian heal circle, a Commander
    /// speed field, etc. Spawned BY THE SERVER (see HeroController.CastSynergyServerRpc);
    /// the NetworkObject spawn replicates any child visuals to every client automatically.
    ///
    /// SECURITY MODEL: this script's logic only ever executes where IsServer is true.
    /// It mutates other players' state exclusively through BaseHero's self-guarding
    /// server mutators (ServerHeal / ServerApplySpeedBuff), which write server-owned
    /// NetworkVariables. A malicious client has no code path into any of this —
    /// equivalent to clients only ever reaching the DB through a validated API layer.
    ///
    /// PREFAB: this + NetworkObject + child visual (sprite ring / particles).
    /// Register in the NetworkManager's Network Prefabs list.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class AreaOfEffectNetworked : NetworkBehaviour
    {
        [Header("Shape & Lifetime")]
        [Min(0.1f)] [SerializeField] private float radius = 3f;
        [Min(0.1f)] [SerializeField] private float duration = 5f;
        [Min(0.1f)] [SerializeField] private float tickInterval = 1f;

        [Header("Effects Per Tick")]
        [SerializeField] private float healPerTick = 5f;
        [Tooltip("1 = no speed change. 1.25 = +25% move speed while buff lasts.")]
        [SerializeField] private float speedMultiplier = 1.25f;
        [Tooltip("Buff outlives the tick slightly so it doesn't flicker between pulses.")]
        [SerializeField] private float speedBuffDuration = 1.5f;

        [Header("Filtering")]
        [Tooltip("Set to the Player layer — this zone only affects heroes.")]
        [SerializeField] private LayerMask affectedLayers;

        private string _casterName = "Unknown";
        private float _elapsed;
        private float _nextTickTime;

        private static readonly Collider[] OverlapBuffer = new Collider[16];
        // One hero = many colliders potentially; dedupe per pulse. Reused to avoid per-tick GC.
        private readonly HashSet<BaseHero> _affectedThisPulse = new();

        /// <summary>Call server-side BEFORE NetworkObject.Spawn() — plain field, not replicated.</summary>
        public void Initialize(string casterName) => _casterName = casterName;

        public override void OnNetworkSpawn()
        {
            // Clients keep the visuals but never run the logic — Update below is server-only.
            if (!IsServer) enabled = false;
        }

        private void Update()
        {
            // (Server-only: enabled=false everywhere else.)
            _elapsed += Time.deltaTime;

            if (Time.time >= _nextTickTime)
            {
                _nextTickTime = Time.time + tickInterval;
                Pulse();
            }

            if (_elapsed >= duration)
            {
                // Despawn(true) destroys the object on server AND all clients.
                NetworkObject.Despawn(true);
            }
        }

        private void Pulse()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, radius, OverlapBuffer, affectedLayers, QueryTriggerInteraction.Collide);

            _affectedThisPulse.Clear();

            for (int i = 0; i < count; i++)
            {
                var hero = OverlapBuffer[i].GetComponentInParent<BaseHero>();
                if (hero == null || !hero.IsAlive || !_affectedThisPulse.Add(hero)) continue;

                // NetworkObjectId is the stable cross-network identity of what we detected —
                // log it so debugging "who got healed" is unambiguous across machines.
                ulong netId = hero.NetworkObjectId;

                hero.ServerHeal(healPerTick);
                if (speedMultiplier > 1f)
                    hero.ServerApplySpeedBuff(speedMultiplier, speedBuffDuration);

                CombatLogManager.LogAction(
                    _casterName, "healed", $"{hero.DisplayName} [NetId:{netId}] for {healPerTick:F0}",
                    transform.position);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 1f, 0.4f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
#endif
    }
}
