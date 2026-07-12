using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Enemies.Spawning
{
    /// <summary>How the enemy physically enters the world during its Spawning state.</summary>
    public enum SpawnEntryStyle
    {
        RiseFromGround,  // Digs out: visual rises from -riseDepth to rest pose (Desert Worm, Shambler)
        DropIn,          // Falls in from +dropHeight with an ease-in slam (Ceiling Spider, drones)
        Materialize,     // Scales 0 → 1 (Void enemies, Family Echoes)
        AnimatorOnly,    // No procedural motion — fires an Animator trigger instead
    }

    /// <summary>
    /// Presentation for BaseEnemy's Spawning state — the visual answer to "no popping".
    ///
    /// NETWORK DESIGN: no ClientRpc, no Animator sync, no extra bytes. BaseEnemy's
    /// IsSpawning NetworkVariable already replicates (its initial value rides inside the
    /// spawn payload itself), so every peer — server, host, clients, LATE JOINERS — sees
    /// the flag and plays the sequence locally. An RPC would arrive as a separate message
    /// that late joiners miss and that can race the spawn; the NetworkVariable can't.
    ///
    /// PROCEDURAL BY DEFAULT: a coroutine lerp over an AnimationCurve — zero animation-file
    /// or DOTween dependencies. Only the visual child moves, in LOCAL space, so the
    /// server-authoritative NetworkTransform on the root is never fought.
    ///
    /// PREFAB SETUP: add next to BaseEnemy. Assign 'visualRoot' = the sprite/mesh child
    /// (NOT the networked root!). Pick a style, tweak curve/depth, optionally drop in a
    /// dirt-burst ParticleSystem. Duration comes from BaseEnemy.SpawnEntryDuration so
    /// gameplay (invulnerability window) and visuals always end on the same frame.
    /// </summary>
    [RequireComponent(typeof(BaseEnemy))]
    public class EnemySpawnEntryPresenter : NetworkBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The visual child that gets animated (sprite/mesh holder). NEVER the " +
                 "networked root — NetworkTransform owns that. Null = first child.")]
        [SerializeField] private Transform visualRoot;

        [Header("Entry")]
        [SerializeField] private SpawnEntryStyle style = SpawnEntryStyle.RiseFromGround;

        [Tooltip("RiseFromGround: how far below the rest pose the visual starts (meters).")]
        [Min(0f)] [SerializeField] private float riseDepth = 2f;

        [Tooltip("DropIn: how far above the rest pose the visual starts (meters).")]
        [Min(0f)] [SerializeField] private float dropHeight = 3f;

        [Tooltip("Progress curve over the entry duration. EaseOut feels like digging, " +
                 "EaseIn like a heavy drop.")]
        [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Extras (optional)")]
        [Tooltip("Played once when the entry starts (dirt burst, teleport glow...). " +
                 "Keep it a child of the enemy so it pools along with it.")]
        [SerializeField] private ParticleSystem entryParticles;

        [Tooltip("AnimatorOnly style / or fired IN ADDITION to a procedural style if assigned.")]
        [SerializeField] private Animator animator;
        [SerializeField] private string animatorTrigger = "SpawnEntry";

        private BaseEnemy _enemy;
        private Vector3 _restLocalPos;
        private Vector3 _restLocalScale;
        private Coroutine _routine;

        // ------------------------------------------------------------------ Lifecycle

        private void Awake()
        {
            _enemy = GetComponent<BaseEnemy>();
            if (visualRoot == null && transform.childCount > 0) visualRoot = transform.GetChild(0);

            if (visualRoot != null)
            {
                _restLocalPos = visualRoot.localPosition;
                _restLocalScale = visualRoot.localScale;
            }
        }

        public override void OnNetworkSpawn()
        {
            _enemy.NetIsSpawning.OnValueChanged += HandleSpawningChanged;

            // Initial value ships with the spawn payload — no OnValueChanged fires for it.
            if (_enemy.IsSpawning) PlayEntry();
            else SnapToRest();
        }

        public override void OnNetworkDespawn()
        {
            _enemy.NetIsSpawning.OnValueChanged -= HandleSpawningChanged;
            StopRoutine();
        }

        private void HandleSpawningChanged(bool previous, bool current)
        {
            if (current) PlayEntry();
            else SnapToRest(); // Safety net: tween and gameplay end together anyway.
        }

        // ------------------------------------------------------------------ Presentation

        private void PlayEntry()
        {
            StopRoutine();

            if (entryParticles != null) entryParticles.Play();
            if (animator != null && !string.IsNullOrEmpty(animatorTrigger))
                animator.SetTrigger(animatorTrigger);

            if (style == SpawnEntryStyle.AnimatorOnly || visualRoot == null) return;

            float duration = Mathf.Max(0.05f, _enemy.SpawnEntryDuration);
            _routine = StartCoroutine(EntryRoutine(duration));
        }

        private IEnumerator EntryRoutine(float duration)
        {
            Vector3 fromPos = _restLocalPos;
            Vector3 fromScale = _restLocalScale;

            switch (style)
            {
                case SpawnEntryStyle.RiseFromGround: fromPos += Vector3.down * riseDepth; break;
                case SpawnEntryStyle.DropIn:         fromPos += Vector3.up * dropHeight; break;
                case SpawnEntryStyle.Materialize:    fromScale = Vector3.zero;            break;
            }

            visualRoot.localPosition = fromPos;
            visualRoot.localScale = fromScale;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = ease.Evaluate(Mathf.Clamp01(elapsed / duration));

                visualRoot.localPosition = Vector3.LerpUnclamped(fromPos, _restLocalPos, t);
                visualRoot.localScale = Vector3.LerpUnclamped(fromScale, _restLocalScale, t);
                yield return null;
            }

            SnapToRest();
        }

        private void SnapToRest()
        {
            StopRoutine();
            if (visualRoot == null) return;
            visualRoot.localPosition = _restLocalPos;
            visualRoot.localScale = _restLocalScale;
        }

        private void StopRoutine()
        {
            if (_routine == null) return;
            StopCoroutine(_routine);
            _routine = null;
        }
    }
}
