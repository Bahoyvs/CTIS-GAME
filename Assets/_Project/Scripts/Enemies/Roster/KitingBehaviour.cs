using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Enemies.Roster
{
    /// <summary>
    /// Hyper-Sprinter: keeps a preferred distance from its target, backpedaling whenever a
    /// hero closes in — while the normal attack loop keeps firing (its EnemyData attack
    /// range exceeds the preferred distance, so retreat and shooting overlap). It doesn't
    /// fight the base state machine; it just re-arms the agent with a flee destination
    /// after the Attack state parks it. CC (stun/root effects) pins it: agent control is
    /// skipped while a slow-immunity-free hard CC has the brain stunned via hitstun.
    /// </summary>
    [RequireComponent(typeof(RosterEnemy))]
    public class KitingBehaviour : NetworkBehaviour
    {
        [Tooltip("Tries to hover at this range from its current target.")]
        [Min(1f)] [SerializeField] private float preferredDistance = 6.5f;

        [Tooltip("Extra meters added to each retreat hop so it doesn't jitter on the line.")]
        [Min(0f)] [SerializeField] private float retreatBuffer = 1.5f;

        private RosterEnemy _enemy;

        private void Awake() => _enemy = GetComponent<RosterEnemy>();

        public override void OnNetworkSpawn()
        {
            if (!IsServer) { enabled = false; return; }
            enabled = true;
        }

        private void Update()
        {
            if (!_enemy.IsAlive || _enemy.IsSpawning || _enemy.BrainSuspended) return;

            var target = _enemy.Target;
            var agent = _enemy.NavAgent;
            if (target == null || !agent.enabled || !agent.isOnNavMesh) return;

            Vector3 away = transform.position - target.transform.position;
            away.y = 0f;
            float dist = away.magnitude;
            if (dist >= preferredDistance) return; // Comfortable — base chase/attack rules apply.

            Vector3 fleeDir = dist > 0.01f ? away / dist : Random.insideUnitSphere.normalized;
            fleeDir.y = 0f;
            Vector3 destination = transform.position + fleeDir * (preferredDistance - dist + retreatBuffer);

            agent.isStopped = false;
            agent.SetDestination(destination);
        }
    }
}
