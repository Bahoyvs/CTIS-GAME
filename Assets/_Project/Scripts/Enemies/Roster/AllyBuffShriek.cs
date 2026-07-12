using Unity.Netcode;
using UnityEngine;
using CBuilding.Core;

namespace CBuilding.Enemies.Roster
{
    /// <summary>
    /// The Screamer: periodic shriek granting nearby zombies (self included) +30% move
    /// speed and slow-immunity for 4 seconds. Makes the Screamer the priority target —
    /// ignore it and the horde accelerates through your kiting.
    /// Buff lands via RosterEnemy's speed registry + GrantSlowImmunity; plain BaseEnemy
    /// neighbours (biome specialists) are unaffected by design.
    /// </summary>
    [RequireComponent(typeof(RosterEnemy))]
    public class AllyBuffShriek : NetworkBehaviour
    {
        [Min(1f)] [SerializeField] private float shriekInterval = 8f;
        [Min(1f)] [SerializeField] private float radius = 8f;
        [Min(0.5f)] [SerializeField] private float buffDuration = 4f;
        [Min(1f)] [SerializeField] private float speedMultiplier = 1.3f;

        private RosterEnemy _self;
        private float _nextShriekTime;

        private void Awake() => _self = GetComponent<RosterEnemy>();

        public override void OnNetworkSpawn()
        {
            if (!IsServer) { enabled = false; return; }
            enabled = true;
            _nextShriekTime = Time.time + shriekInterval * 0.5f; // First shriek arrives fast-ish.
        }

        private void Update()
        {
            if (!_self.IsAlive || _self.IsSpawning || _self.BrainSuspended) return;
            if (Time.time < _nextShriekTime) return;
            _nextShriekTime = Time.time + shriekInterval;

            int buffed = 0;
            // MVP-scale scan, same trade-off as EnemyRegistry — revisit if counts explode.
            foreach (RosterEnemy ally in FindObjectsByType<RosterEnemy>(FindObjectsSortMode.None))
            {
                if (ally == null || !ally.IsAlive || ally.IsSpawning) continue;
                if ((ally.transform.position - transform.position).sqrMagnitude > radius * radius) continue;

                ally.AddSpeedMultiplier(this, speedMultiplier, buffDuration);
                ally.GrantSlowImmunity(buffDuration);
                buffed++;
            }

            if (buffed > 0)
                CombatLogManager.LogAction(_self.DisplayName, "used",
                    $"Shriek buffing {buffed} zombie(s)", transform.position);
        }
    }
}
