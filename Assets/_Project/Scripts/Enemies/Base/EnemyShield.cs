using Unity.Netcode;
using UnityEngine;
using CBuilding.Core;

namespace CBuilding.Enemies
{
    /// <summary>
    /// Generic absorbing shield pool for enemies — the "enemy shield system" the
    /// EnemyWorldUI shield strip was reserved for. An IDamageModifier at the END of the
    /// chain (priority 250): marks/vulnerabilities compute the real hit first, the shield
    /// eats what's left. Alarm-Bringer grants these (450 self / 250 nearby) on its siren.
    ///
    /// Present on every roster prefab at 0; ServerAddShield activates it. Value lives in
    /// a NetworkVariable so UI can bind on any peer. Optional expiry per grant.
    /// Pool-safe: zeroed on every OnNetworkSpawn.
    /// </summary>
    public class EnemyShield : NetworkBehaviour, IDamageModifier
    {
        public int Priority => 250;

        private readonly NetworkVariable<float> _netShield = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public float Current => _netShield.Value;

        /// <summary>UI binding surface — subscribe to OnValueChanged, never poll.</summary>
        public NetworkVariable<float> NetShield => _netShield;

        private DamageModifierPipeline _pipeline;
        private float _expiryTime;

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            _netShield.Value = 0f;
            _expiryTime = 0f;
            _pipeline = GetComponent<DamageModifierPipeline>();
            _pipeline?.Register(this); // Register() dedupes across pooled lives.
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) _pipeline?.Unregister(this);
        }

        private void Update()
        {
            if (!IsServer || _netShield.Value <= 0f) return;
            if (_expiryTime > 0f && Time.time >= _expiryTime) _netShield.Value = 0f;
        }

        /// <summary>Server-only. duration &lt;= 0 = lasts until consumed.</summary>
        public void ServerAddShield(float amount, float duration = 0f)
        {
            if (!IsServer || amount <= 0f) return;
            _netShield.Value += amount;
            if (duration > 0f) _expiryTime = Time.time + duration;
        }

        public float Modify(in DamageInfo info, float currentAmount)
        {
            if (info.IsHealing || currentAmount <= 0f || _netShield.Value <= 0f) return currentAmount;

            float absorbed = Mathf.Min(_netShield.Value, currentAmount);
            _netShield.Value -= absorbed;
            return currentAmount - absorbed;
        }
    }
}
