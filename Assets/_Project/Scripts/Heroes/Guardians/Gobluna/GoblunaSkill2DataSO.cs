using CBuilding.Abilities;
using CBuilding.Abilities.Delivery;
using UnityEngine;

namespace CBuilding.Heroes.Gobluna
{
    /// <summary>
    /// Skill2: Green Fire Purge &amp; Stun. NO base cooldown — availability is gated
    /// entirely by GoblunaSkill2Controller's lock/resource state machine (see that class
    /// for the full design note). This asset only carries the composable cone and the
    /// runtime binding, mirroring BahadirSkill2DataSO's shape.
    /// </summary>
    [CreateAssetMenu(menuName = "CBuilding/Abilities/Heroes/Gobluna/Skill2", fileName = "Ability_Gobluna_Skill2")]
    public class GoblunaSkill2DataSO : AbilityDataSO
    {
        [Header("Composable cone (the Step-1 cast)")]
        [Tooltip("CA_Gobluna_S2_Cone — Arc delivery (~180°), ApplyStatus(Fx_GreenFire) → Enemies, TeamFilter = Enemies.")]
        public ComposedAbilitySO coneAbility;

        public override AbilityRuntime CreateRuntime() => new GoblunaSkill2Runtime();

        protected override void OnValidate()
        {
            base.OnValidate();
            mode = AbilityMode.Instant;
            cooldown = 0f; // the lock IS the cooldown
        }
    }
}
