using System.Collections;
using CBuilding.Abilities;
using CBuilding.Heroes;
using CBuilding.StatusEffects;
using UnityEngine;

namespace CBuilding.Heroes.Gobluna
{
    /// <summary>
    /// Logic half of <see cref="GoblunaFeatureDataSO"/>. Server-side sequencing:
    /// pick ally → root self → owner-RPC the dash (via GoblunaHeroController) →
    /// wait the flight time → drop the composable heal zone at the ally's CURRENT
    /// position (re-read at arrival, so a moving ally keeps the circle).
    /// </summary>
    public class GoblunaFeatureRuntime : AbilityRuntime
    {
        private GoblunaHeroController _gobluna;
        private Coroutine _leapRoutine;

        protected override void OnInitialize()
        {
            _gobluna = Controller.GetComponent<GoblunaHeroController>();
            if (_gobluna == null)
            {
                Debug.LogError(
                    "[GoblunaFeatureRuntime] Gobluna's prefab needs a GoblunaHeroController " +
                    "next to AbilityController — Feature will refuse to cast without it.",
                    Controller);
            }
        }

        /// <summary>No leapable ally = the cast is refused BEFORE the cooldown commits.</summary>
        public override bool CanActivate() => _gobluna != null && FindLeapTarget() != null;

        public override void Execute()
        {
            var data = (GoblunaFeatureDataSO)Data;

            BaseHero ally = FindLeapTarget();
            if (ally == null) return; // CanActivate raced an ally death — harmless no-op

            if (data.selfRootEffect != null &&
                Controller.TryGetComponent<StatusEffectController>(out var status))
            {
                status.ApplyEffect(data.selfRootEffect, Controller.gameObject);
            }

            _gobluna.ServerBeginLeap(ally.transform.position, data.leapDuration);

            if (_leapRoutine != null) Controller.StopCoroutine(_leapRoutine);
            _leapRoutine = Controller.StartCoroutine(DropZoneAfterFlight(ally, data));
        }

        private IEnumerator DropZoneAfterFlight(BaseHero ally, GoblunaFeatureDataSO data)
        {
            yield return new WaitForSeconds(data.leapDuration);
            _leapRoutine = null;

            // Ally may have moved (or died) during the flight — drop where they ARE,
            // falling back to wherever Gobluna landed.
            Vector3 dropPoint = ally != null && ally.IsAlive
                ? ally.transform.position
                : Controller.transform.position;

            data.healZoneAbility?.ExecuteDelivery(Controller, dropPoint);
        }

        /// <summary>
        /// Leapable ally: living hero, not Gobluna, within leapRange of HER. Among valid
        /// candidates, the one closest to the AIM POINT wins — aiming at a specific
        /// teammate must beat raw proximity, or she could never choose her leap target.
        /// </summary>
        private BaseHero FindLeapTarget()
        {
            var data = (GoblunaFeatureDataSO)Data;
            Vector3 origin = Controller.transform.position;
            Vector3 aim = Controller.CurrentAimPoint;
            float rangeSqr = data.leapRange * data.leapRange;

            BaseHero best = null;
            float bestAimSqr = float.MaxValue;

            for (int i = 0; i < BaseHero.ActiveHeroes.Count; i++)
            {
                BaseHero hero = BaseHero.ActiveHeroes[i];
                if (hero == null || !hero.IsAlive || hero.gameObject == Controller.gameObject) continue;
                if ((hero.transform.position - origin).sqrMagnitude > rangeSqr) continue;

                float aimSqr = (hero.transform.position - aim).sqrMagnitude;
                if (aimSqr < bestAimSqr)
                {
                    bestAimSqr = aimSqr;
                    best = hero;
                }
            }
            return best;
        }
    }
}
