using System;
using CBuilding.Enemies;
using UnityEngine;

namespace CBuilding.StatusEffects
{
    /// <summary>
    /// GS-9 — Bahadır's Spyware mark. The damage-modifier (incomingDamageMultiplier) and
    /// chip DoT (damagePerTick) parts are fully data-driven and already covered by
    /// GenericStatusEffect — set them on the EffectDataSO fields in the Inspector. The ONLY
    /// genuinely new behaviour is the death hook: Skill2's "virus returns to Bahadır when a
    /// marked target dies" mechanic needs to know exactly when a MARKED enemy dies, which
    /// nothing in the generic status/damage pipeline exposes.
    ///
    /// Subclasses GenericStatusEffect (not IStatusEffect directly) purely to inherit the
    /// damage-modifier/DoT behaviour for free — this class only adds the death subscription.
    /// </summary>
    [CreateAssetMenu(menuName = "CBuilding/Status Effects/Spyware Mark", fileName = "Fx_Spyware")]
    public class SpywareMarkEffectSO : EffectDataSO
    {
        public override IStatusEffect CreateRuntime() => new SpywareMarkStatus(this);
    }

    /// <summary>
    /// Public (not nested) so <see cref="CBuilding.Enemies.EnemyRegistry"/> and hero runtimes
    /// can query/filter for it by type via StatusEffectController.GetActiveEffectOfType&lt;T&gt;.
    /// </summary>
    public class SpywareMarkStatus : GenericStatusEffect
    {
        /// <summary>(source who applied the mark, the enemy that died while marked). Server-only.</summary>
        public static event Action<GameObject, GameObject> OnMarkedTargetDied;

        private BaseEnemy _markedEnemy;
        private GameObject _markSource;

        public SpywareMarkStatus(EffectDataSO data) : base(data) { }

        public override void OnApply(StatusEffectContext context)
        {
            base.OnApply(context);

            _markSource = context.Source;
            if (context.Target.TryGetComponent(out _markedEnemy))
            {
                _markedEnemy.OnDied += HandleMarkedTargetDied;
            }
        }

        public override void OnExpire(StatusEffectContext context)
        {
            base.OnExpire(context);
            Unsubscribe();
        }

        private void HandleMarkedTargetDied(BaseEnemy enemy)
        {
            OnMarkedTargetDied?.Invoke(_markSource, enemy.gameObject);
            Unsubscribe();
        }

        private void Unsubscribe()
        {
            if (_markedEnemy != null) _markedEnemy.OnDied -= HandleMarkedTargetDied;
            _markedEnemy = null;
        }
    }
}
