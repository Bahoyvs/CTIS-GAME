using UnityEngine;
using CBuilding.Core;

namespace CBuilding.Enemies.Roster
{
    /// <summary>
    /// Alarm-Bringer: every 15 seconds the siren arms; the FIRST hit taken while armed
    /// triggers it — 450 HP shield on itself, 250 HP shield on every nearby zombie
    /// (anything carrying an EnemyShield). Punishes mindless focus-fire into the pack;
    /// rewards bursting it between sirens or pulling it away from the horde first.
    /// Starts armed. Server-only by construction (OnDamaged never fires on clients).
    /// </summary>
    [RequireComponent(typeof(RosterEnemy))]
    [RequireComponent(typeof(EnemyShield))]
    public class SirenShieldOnHit : MonoBehaviour
    {
        [Min(1f)] [SerializeField] private float armInterval = 15f;
        [Min(0f)] [SerializeField] private float selfShield = 450f;
        [Min(0f)] [SerializeField] private float allyShield = 250f;
        [Min(1f)] [SerializeField] private float radius = 7f;
        [Tooltip("Granted shields fade after this long if not consumed. 0 = permanent.")]
        [Min(0f)] [SerializeField] private float shieldDuration = 10f;

        private RosterEnemy _enemy;
        private EnemyShield _ownShield;
        private bool _armed;
        private float _rearmTime;

        private void Awake()
        {
            _enemy = GetComponent<RosterEnemy>();
            _ownShield = GetComponent<EnemyShield>();
        }

        private void OnEnable()
        {
            _armed = true; // Pool-safe: every life starts with the siren ready.
            _enemy.OnDamaged += HandleDamaged;
        }

        private void OnDisable() => _enemy.OnDamaged -= HandleDamaged;

        private void Update()
        {
            if (!_armed && Time.time >= _rearmTime) _armed = true;
        }

        private void HandleDamaged(DamageInfo info)
        {
            if (!_armed || info.IsHealing) return;

            _armed = false;
            _rearmTime = Time.time + armInterval;

            _ownShield.ServerAddShield(selfShield, shieldDuration);

            int shielded = 0;
            foreach (RosterEnemy ally in FindObjectsByType<RosterEnemy>(FindObjectsSortMode.None))
            {
                if (ally == null || ally == _enemy || !ally.IsAlive || ally.IsSpawning) continue;
                if ((ally.transform.position - transform.position).sqrMagnitude > radius * radius) continue;
                if (!ally.TryGetComponent(out EnemyShield shield)) continue;

                shield.ServerAddShield(allyShield, shieldDuration);
                shielded++;
            }

            CombatLogManager.LogAction(_enemy.DisplayName, "used",
                $"Siren shielding self + {shielded} zombie(s)", transform.position);
        }
    }
}
