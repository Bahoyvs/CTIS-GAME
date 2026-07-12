using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace CBuilding.Core
{
    /// <summary>
    /// Central "juice" service: hitstop, screen shake dispatch, knockback helpers.
    /// Singleton (like a process-wide service in a Node app) — combat code calls
    /// GameFeelManager.Instance.* without needing scene references.
    ///
    /// Place one instance on a persistent GameObject (e.g. "_Managers").
    /// </summary>
    public class GameFeelManager : MonoBehaviour
    {
        public static GameFeelManager Instance { get; private set; }

        [Header("Hitstop")]
        [Tooltip("Time scale during hitstop. 0 = full freeze; ~0.05 keeps a sliver of motion.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float hitstopTimeScale = 0.05f;

        [Header("Default Impact Values")]
        [SerializeField] private float lightHitstopDuration = 0.05f;
        [SerializeField] private float heavyHitstopDuration = 0.12f;
        [Tooltip("Minimum realtime seconds between hitstops to prevent rapid-fire stuttering.")]
        [SerializeField] private float hitstopCooldown = 0.5f;

        /// <summary>
        /// Screen shake hook. Decoupled via event so this script has no hard Cinemachine
        /// dependency — CinemachineShakeAdapter (or any listener) subscribes and translates
        /// intensity into an Impulse. Same pattern as an event emitter between services.
        /// </summary>
        public static event Action<float> OnScreenShakeRequested;

        private Coroutine _hitstopRoutine;
        private float _hitstopEndTime; // Realtime, since timeScale is frozen during hitstop.
        private float _lastHitstopTime = -1f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                Time.timeScale = 1f; // Never leave the game frozen if the manager dies mid-hitstop.
            }
        }

        // ---------------------------------------------------------------- Hitstop

        public void DoLightHitstop() => DoHitstop(lightHitstopDuration);
        public void DoHeavyHitstop() => DoHitstop(heavyHitstopDuration);

        /// <summary>
        /// Freezes Time.timeScale for a few ms. Overlapping requests extend rather than
        /// stack, so mashing attacks can't chain-freeze the game.
        /// Includes a debounce to prevent rapid-fire hitstutters.
        /// </summary>
        public void DoHitstop(float duration)
        {
            if (Time.realtimeSinceStartup - _lastHitstopTime < hitstopCooldown) return;

            float requestedEnd = Time.realtimeSinceStartup + duration;
            if (requestedEnd <= _hitstopEndTime) return; // Already frozen longer than this.

            _lastHitstopTime = Time.realtimeSinceStartup;
            _hitstopEndTime = requestedEnd;
            if (_hitstopRoutine == null)
                _hitstopRoutine = StartCoroutine(HitstopRoutine());
        }

        private IEnumerator HitstopRoutine()
        {
            Time.timeScale = hitstopTimeScale;
            // WaitForSecondsRealtime is mandatory here: scaled WaitForSeconds would (almost) never
            // finish while timeScale is ~0.
            while (Time.realtimeSinceStartup < _hitstopEndTime)
                yield return null;

            Time.timeScale = 1f;
            _hitstopRoutine = null;
        }

        // ---------------------------------------------------------------- Screen Shake

        /// <summary>Intensity is unit-less; listeners map it to impulse force. ~0.5 light, ~2 heavy.</summary>
        public void RequestScreenShake(float intensity)
        {
            OnScreenShakeRequested?.Invoke(intensity);
        }

        // ---------------------------------------------------------------- Knockback

        /// <summary>Physics-driven knockback for Rigidbody actors (e.g. the hero, props).</summary>
        public void ApplyKnockback(Rigidbody body, Vector3 direction, float force)
        {
            if (body == null || force <= 0f) return;
            direction.y = 0f; // Keep actors glued to the ground plane.
            body.AddForce(direction.normalized * force, ForceMode.Impulse);
        }

        /// <summary>
        /// Knockback for NavMeshAgent actors. Agents ignore physics forces, so we manually
        /// displace them with agent.Move() over a short duration (with ease-out decay).
        /// agent.Move() still respects NavMesh edges — enemies can't be punched through walls.
        /// </summary>
        public void ApplyKnockback(NavMeshAgent agent, Vector3 direction, float force, float duration = 0.15f)
        {
            if (agent == null || force <= 0f || duration <= 0f) return;
            direction.y = 0f;
            StartCoroutine(AgentKnockbackRoutine(agent, direction.normalized, force, duration));
        }

        private static IEnumerator AgentKnockbackRoutine(NavMeshAgent agent, Vector3 direction, float force, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                // Agent may be destroyed/disabled mid-knockback (death) — bail safely.
                if (agent == null || !agent.enabled || !agent.isOnNavMesh) yield break;

                elapsed += Time.deltaTime;
                float decay = 1f - (elapsed / duration);           // Linear ease-out.
                agent.Move(direction * (force * decay * Time.deltaTime));
                yield return null;
            }
        }
    }
}
