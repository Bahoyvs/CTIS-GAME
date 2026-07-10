using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Abilities.Delivery
{
    /// <summary>
    /// Spawns a persistent networked zone at the (range-clamped) aim point that applies
    /// the ability's effect list to valid targets every tick.
    ///   Gobluna S2  = green fire (Damage + burn status per tick)
    ///   Ironworks S1= Hex-Shield (damage-reduction status → Allies per tick)
    ///   Ug Ult      = wind tunnel (growing shield status → Allies + Displacement → Enemies)
    ///   Kerem Ult trail = several small short-lived zones dropped along the path
    /// Generalizes AreaOfEffectNetworked — new zones need assets, not new scripts.
    /// </summary>
    [CreateAssetMenu(menuName = "CBuilding/Abilities/Deliveries/Zone (Persistent)", fileName = "Del_Zone")]
    public class ZoneDeliverySO : AbilityDeliverySO
    {
        [Header("Prefab (AbilityZone + NetworkObject + visual, in Network Prefabs list)")]
        public NetworkObject zonePrefab;

        [Header("Placement")]
        [Tooltip("Max cast distance; aim point clamped into this. 0 = at the caster's feet.")]
        [Min(0f)] public float castRange = 6f;

        [Header("Zone behaviour")]
        [Min(0.1f)] public float radius = 3f;
        [Min(0.1f)] public float duration = 5f;
        [Min(0.1f)] public float tickInterval = 1f;
        public LayerMask hitLayers = ~0;

        public override void Execute(in AbilityCastContext ctx)
        {
            if (zonePrefab == null)
            {
                Debug.LogWarning($"[{name}] No zonePrefab assigned.");
                return;
            }

            Vector3 toAim = ctx.AimPoint - ctx.Origin;
            toAim.y = 0f;
            Vector3 pos = ctx.Origin + Vector3.ClampMagnitude(toAim, castRange);

            NetworkObject instance = Object.Instantiate(zonePrefab, pos, Quaternion.identity);
            if (instance.TryGetComponent<AbilityZone>(out var zone))
            {
                zone.ServerConfigure(ctx.Ability, this, ctx.Caster); // BEFORE Spawn
            }
            instance.Spawn(true);
        }
    }
}
