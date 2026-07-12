using System.Collections;
using CBuilding.Abilities;
using CBuilding.Core;
using CBuilding.StatusEffects;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Heroes.Gobluna
{
    /// <summary>
    /// GS-9 — Gobluna's bespoke sibling controller (same pattern as BahadirGhostController:
    /// a component NEXT TO HeroController/AbilityController, never a HeroController subclass).
    /// Owns the three things her kit can't express as pure assets:
    ///
    ///   1. SIPHONER passive — TeamEventBus.OnAllyDealtDamage (she dealt damage) → heal
    ///      heroes near the victim, scaled by the post-pipeline damage. Also stamps the
    ///      permanent GREEN FIRE mark on everything she damages.
    ///   2. ULTIMATE mode driver — watches her own StatusEffectController for
    ///      Effect_GoblunaUltMode: while active, Skill1's cooldown is overridden to 0.4s
    ///      (CooldownManager.SetCooldownOverride) and an AllyBounceProjectile blast is
    ///      emitted every blastInterval seconds. The Ultimate CAST itself is a plain
    ///      ComposedAbilitySO (Self delivery + full Heal + ApplyStatus(UltMode)) — this
    ///      class only reacts to the status, so the cast stays 100% data-driven.
    ///   3. FEATURE leap motion — movement is owner-authoritative (ClientNetworkTransform),
    ///      so the server-side GoblunaFeatureRuntime asks THIS class to RPC the owner into
    ///      a short CharacterController dash toward the ally.
    ///
    /// PREFAB SETUP: Hero_Gobluna = HeroController + AbilityController + StatusEffectController
    /// + HeroStatController + ComposedBasicAttackBehaviour + GoblunaSkill2Controller + THIS.
    /// All logic hooks are server-only; the leap RPC is the single owner-side piece.
    /// </summary>
    [RequireComponent(typeof(AbilityController))]
    [RequireComponent(typeof(StatusEffectController))]
    public class GoblunaHeroController : NetworkBehaviour
    {
        [Header("Siphoner passive")]
        [Tooltip("Radius of the ally search around the DAMAGED TARGET (not around Gobluna).")]
        [Min(0.1f)] [SerializeField] private float siphonRadius = 4f;
        [Tooltip("Heal per point of damage dealt (0.3 = 30% of damage as healing to each nearby ally).")]
        [Range(0f, 2f)] [SerializeField] private float siphonRatio = 0.3f;
        [Tooltip("Gobluna herself benefits if she is near the victim (ranged Guardian: usually only in melee emergencies).")]
        [SerializeField] private bool siphonHealsSelf = true;

        [Header("Green Fire passive (permanent mark)")]
        [Tooltip("Fx_GreenFireMark — permanent, non-DoT mark stamped on every enemy she damages. Distinct asset from Skill2's Fx_GreenFire DoT, or the S2 lock would never open.")]
        [SerializeField] private EffectDataSO greenFireMark;

        [Header("Ultimate — Bouncing Blessing")]
        [Tooltip("Effect_GoblunaUltMode (duration 18s, no flags, isDebuff off). The Ultimate CA applies it to self; this controller reacts to apply/expire.")]
        [SerializeField] private EffectDataSO ultModeEffect;
        [Tooltip("Skill1 cooldown while Ult mode is active (kit spec: 0.4s).")]
        [Min(0.05f)] [SerializeField] private float ultSkill1Cooldown = 0.4f;
        [Tooltip("Seconds between bouncing blasts during the mode (kit spec: 5s; the first fires immediately on cast).")]
        [Min(0.5f)] [SerializeField] private float blastInterval = 5f;
        [Tooltip("AllyBounceProjectile prefab (+ NetworkObject + NetworkTransform), registered in the Network Prefabs list.")]
        [SerializeField] private NetworkObject allyBounceProjectilePrefab;

        private AbilityController _abilities;
        private StatusEffectController _status;
        private BaseHero _hero;
        private Coroutine _ultLoop;
        private float _ultModeEndTime;

        private void Awake()
        {
            _abilities = GetComponent<AbilityController>();
            _status = GetComponent<StatusEffectController>();
            _hero = GetComponent<BaseHero>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            TeamEventBus.OnAllyDealtDamage += HandleAllyDealtDamage;
            _status.OnEffectApplied += HandleEffectApplied;
            _status.OnEffectExpired += HandleEffectExpired;
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer) return;

            TeamEventBus.OnAllyDealtDamage -= HandleAllyDealtDamage;
            _status.OnEffectApplied -= HandleEffectApplied;
            _status.OnEffectExpired -= HandleEffectExpired;
            StopUltimateMode(); // despawn mid-mode: never leave a dangling CD override
        }

        // ---------------------------------------------------------------- Passives (SERVER)

        /// <summary>
        /// One handler covers BOTH passives, for EVERY damage source she has (basic orb,
        /// darts, green fire DoT ticks, ult blast path damage) — they all funnel through
        /// BaseEnemy.TakeDamage → TeamEventBus, so nothing here cares which ability hit.
        /// </summary>
        private void HandleAllyDealtDamage(GameObject ally, GameObject victim, float amount)
        {
            if (ally != gameObject || _hero == null || !_hero.IsAlive) return;

            // Green Fire: permanent mark (StackingPolicy.Ignore on the asset makes re-hits free).
            if (greenFireMark != null && victim.TryGetComponent<StatusEffectController>(out var victimStatus))
            {
                victimStatus.ApplyEffect(greenFireMark, gameObject);
            }

            // Siphoner: heal heroes near the VICTIM. Iterates the GS-16 spawn registry
            // instead of Physics.OverlapSphere — heroes are a handful of known objects,
            // and the registry can't miss due to a layer-mask misconfiguration.
            float healAmount = amount * siphonRatio;
            if (healAmount <= 0f) return;

            Vector3 center = victim.transform.position;
            float sqrRadius = siphonRadius * siphonRadius;

            for (int i = 0; i < BaseHero.ActiveHeroes.Count; i++)
            {
                BaseHero hero = BaseHero.ActiveHeroes[i];
                if (hero == null || !hero.IsAlive) continue;
                if (!siphonHealsSelf && hero.gameObject == gameObject) continue;
                if ((hero.transform.position - center).sqrMagnitude > sqrRadius) continue;

                float healed = hero.ServerHeal(healAmount);
                if (healed > 0f)
                {
                    // Feeds the Skill2 resource bar through the same pipeline as HealEffectSO.
                    TeamEventBus.RaiseAllyHealedAlly(gameObject, hero.gameObject, healed);
                }
            }
        }

        // ---------------------------------------------------------------- Ultimate mode (SERVER)

        private void HandleEffectApplied(EffectDataSO data)
        {
            if (data != ultModeEffect || ultModeEffect == null) return;
            StartUltimateMode();
        }

        private void HandleEffectExpired(EffectDataSO data)
        {
            if (data != ultModeEffect || ultModeEffect == null) return;
            StopUltimateMode();
        }

        private void StartUltimateMode()
        {
            _ultModeEndTime = Time.time + ultModeEffect.duration;
            _abilities.Cooldowns.SetCooldownOverride(AbilitySlot.Skill1, ultSkill1Cooldown);
            // A cooldown committed at the OLD duration keeps running; shave it so the new
            // cadence applies instantly, not after the last long cooldown finishes.
            TrimSkill1Cooldown();

            if (_ultLoop != null) StopCoroutine(_ultLoop);
            _ultLoop = StartCoroutine(BlastLoop());
        }

        private void TrimSkill1Cooldown()
        {
            float remaining = _abilities.Cooldowns.GetRemaining(AbilitySlot.Skill1);
            if (remaining > ultSkill1Cooldown)
            {
                _abilities.Cooldowns.Refund(AbilitySlot.Skill1, remaining - ultSkill1Cooldown);
            }
        }

        private void StopUltimateMode()
        {
            _abilities.Cooldowns.ClearCooldownOverride(AbilitySlot.Skill1);
            if (_ultLoop != null)
            {
                StopCoroutine(_ultLoop);
                _ultLoop = null;
            }
        }

        private IEnumerator BlastLoop()
        {
            // First blast fires the instant the mode starts (kit: "fires a slow projectile
            // to the nearest ally" on cast), then one every blastInterval for the duration.
            SpawnBlast();

            var wait = new WaitForSeconds(blastInterval);
            while (Time.time + 0.01f < _ultModeEndTime)
            {
                yield return wait;
                if (Time.time >= _ultModeEndTime) break;
                SpawnBlast();
            }
            _ultLoop = null;
        }

        private void SpawnBlast()
        {
            if (allyBounceProjectilePrefab == null)
            {
                Debug.LogWarning("[GoblunaHeroController] No allyBounceProjectilePrefab assigned.");
                return;
            }

            Vector3 spawnPos = transform.position + Vector3.up * 0.5f;
            NetworkObject instance = Instantiate(allyBounceProjectilePrefab, spawnPos, Quaternion.identity);

            if (instance.TryGetComponent<AllyBounceProjectile>(out var blast))
            {
                // Every blast dies with the mode: a t=15s blast lives 3s, not a full 18.
                blast.ServerConfigure(gameObject, _ultModeEndTime - Time.time); // BEFORE Spawn
            }
            instance.Spawn(true);
        }

        // ---------------------------------------------------------------- Feature leap (SERVER → OWNER)

        /// <summary>
        /// Server-only, called by GoblunaFeatureRuntime. Movement is owner-authoritative
        /// (ClientNetworkTransform), so the server cannot teleport her — it TELLS the owner
        /// to dash. The runtime pairs this with a self-Root status of the same duration so
        /// regular WASD input doesn't fight the leap.
        /// </summary>
        public void ServerBeginLeap(Vector3 destination, float duration)
        {
            if (!IsServer) return;
            LeapRpc(destination, duration);
        }

        [Rpc(SendTo.Owner)]
        private void LeapRpc(Vector3 destination, float duration)
        {
            StartCoroutine(LeapRoutine(destination, duration));
        }

        private IEnumerator LeapRoutine(Vector3 destination, float duration)
        {
            var cc = GetComponent<CharacterController>();
            if (cc == null || duration <= 0f) yield break;

            Vector3 start = transform.position;
            destination.y = start.y; // stay on the ground plane; gravity handles the rest
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                Vector3 target = Vector3.Lerp(start, destination, Mathf.Clamp01(elapsed / duration));
                cc.Move(target - transform.position); // ClientNetworkTransform replicates
                yield return null;
            }
        }
    }
}
