using Unity.Netcode;
using UnityEngine;
using CBuilding.Core;
using CBuilding.Heroes;

namespace CBuilding.Enemies.Roster
{
    /// <summary>
    /// Stalker-Stitch: invisible until a player wanders inside its reveal radius (or
    /// something damages it — stray AoE counts). Reveal is one-way per life. This is
    /// exactly the "hidden elite in the dark corridor" the Defender's Marking ability
    /// exists to expose — Marking can force-reveal via <see cref="ServerReveal"/>.
    ///
    /// Hidden = visual root + world UI disabled on EVERY peer (NetworkVariable, so late
    /// joiners agree). The server brain still runs: it stalks while unseen. Colliders
    /// stay on, so blind hits both land and reveal.
    /// </summary>
    [RequireComponent(typeof(BaseEnemy))]
    public class StealthUntilClose : NetworkBehaviour
    {
        [Min(0.5f)] [SerializeField] private float revealRadius = 4f;

        [Header("Hidden while stealthed")]
        [SerializeField] private GameObject visualRoot;
        [SerializeField] private GameObject worldUI;

        private readonly NetworkVariable<bool> _netHidden = new(
            true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public bool IsHidden => _netHidden.Value;

        private BaseEnemy _enemy;

        private void Awake() => _enemy = GetComponent<BaseEnemy>();

        public override void OnNetworkSpawn()
        {
            _netHidden.OnValueChanged += HandleHiddenChanged;

            if (IsServer)
            {
                _netHidden.Value = true; // Pool-safe: every life starts cloaked.
                _enemy.OnDamaged += HandleDamaged;
            }

            ApplyVisibility(_netHidden.Value);
        }

        public override void OnNetworkDespawn()
        {
            _netHidden.OnValueChanged -= HandleHiddenChanged;
            if (IsServer) _enemy.OnDamaged -= HandleDamaged;
        }

        private void Update()
        {
            if (!IsServer || !_netHidden.Value || !_enemy.IsAlive || _enemy.IsSpawning) return;

            foreach (BaseHero hero in BaseHero.ActiveHeroes)
            {
                if (hero == null || !hero.IsAlive) continue;
                if ((hero.transform.position - transform.position).sqrMagnitude
                    <= revealRadius * revealRadius)
                {
                    ServerReveal();
                    return;
                }
            }
        }

        private void HandleDamaged(DamageInfo info) => ServerReveal();

        /// <summary>Server-only. Defender's Marking calls this to expose hidden elites.</summary>
        public void ServerReveal()
        {
            if (!IsServer || !_netHidden.Value) return;
            _netHidden.Value = false;
            CombatLogManager.LogAction(_enemy.DisplayName, "was", "revealed", transform.position);
        }

        private void HandleHiddenChanged(bool previous, bool current) => ApplyVisibility(current);

        private void ApplyVisibility(bool hidden)
        {
            if (visualRoot != null) visualRoot.SetActive(!hidden);
            if (worldUI != null) worldUI.SetActive(!hidden);
        }
    }
}
