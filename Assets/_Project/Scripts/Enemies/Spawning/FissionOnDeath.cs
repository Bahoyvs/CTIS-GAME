using UnityEngine;

namespace CBuilding.Enemies.Spawning
{
    /// <summary>
    /// Fission Spawn mechanic (1 → 2 → 4): on death, this enemy splits into N children
    /// pulled from the NetworkEnemyPool — zero Instantiate cost per split. Chain depth is
    /// authored by prefab wiring: Fission → 2x Mid-Spawn (has this component) → 2x
    /// Micro-Spawn (doesn't). Also reusable for Xenomorph Strain clone-on-death variants.
    ///
    /// Children register with the SpawnDirector so Threat Capacity stays honest — a split
    /// swarm still counts against the budget.
    /// </summary>
    [RequireComponent(typeof(BaseEnemy))]
    public class FissionOnDeath : MonoBehaviour
    {
        [Tooltip("Enemy spawned per split. MUST be prewarmed in NetworkEnemyPool (extraPrefabs).")]
        [SerializeField] private BaseEnemy childPrefab;

        [Min(1)] [SerializeField] private int childCount = 2;

        [Tooltip("Threat cost EACH child occupies in the Director's budget.")]
        [Min(0f)] [SerializeField] private float childThreatCost = 0.5f;

        [Tooltip("Children scatter this far from the death position.")]
        [Min(0f)] [SerializeField] private float scatterRadius = 1.2f;

        private BaseEnemy _enemy;

        private void Awake()
        {
            _enemy = GetComponent<BaseEnemy>();
        }

        private void OnEnable()  => _enemy.OnDied += HandleDied;   // Pool-safe: re-hooks every life.
        private void OnDisable() => _enemy.OnDied -= HandleDied;

        private void HandleDied(BaseEnemy enemy)
        {
            if (childPrefab == null || SpawnDirector.Instance == null) return;

            for (int i = 0; i < childCount; i++)
            {
                // Even ring around the corpse so children don't stack inside each other.
                float angle = (360f / childCount) * i * Mathf.Deg2Rad;
                Vector3 offset = new(Mathf.Cos(angle) * scatterRadius, 0f, Mathf.Sin(angle) * scatterRadius);

                SpawnDirector.Instance.ServerSpawnAt(childPrefab, enemy.transform.position + offset, childThreatCost);
            }
        }
    }
}
