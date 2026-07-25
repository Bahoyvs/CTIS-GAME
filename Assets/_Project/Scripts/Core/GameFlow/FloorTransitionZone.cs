using System.Collections;
using System.Collections.Generic;
using CBuilding.Heroes;
using CBuilding.UI;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Core
{
    /// <summary>
    /// GS-2 GameFlow — Inter-Floor "Airlock/Elevator" convergence zone.
    ///
    /// Placed at the END of a floor (e.g. Level_Sec01/Basement/TransitionZone_ToGround).
    /// Floors live far apart on X/Z in the SAME scene, so a transition is just a
    /// synchronized teleport — no scene load, and deliberately NO SectionManager writes
    /// (floors are spatial, sections are logical run-state).
    ///
    /// AUTHORITY MODEL (same "server validates, client renders" contract as VendingMachine):
    ///   - Server owns the truth: which ALIVE heroes stand inside the trigger, and whether
    ///     the zone is armed. Replicated via NetworkVariables (server-write, everyone-read).
    ///   - Clients only render the "Hold E" prompt and forward an activation REQUEST;
    ///     the server re-validates before executing (never trust the client's claim).
    ///
    /// FLOW: all alive heroes inside → _netReady=true → any alive owner inside holds E
    /// → RequestTransitionRpc → server re-validates → fade out on every peer → teleport
    /// EVERYONE (dead ones too, staying dead — GDD floor rule) → fade back in.
    ///
    /// Teleport uses the owner-targeted RPC pattern from SectionManager/FinaleManager:
    /// movement is owner-authoritative (ClientNetworkTransform), so position must be
    /// written ON THE OWNING CLIENT and replicate outward from there.
    ///
    /// SETUP: NetworkObject + BoxCollider (isTrigger). See inspector tooltips.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Collider))]
    public class FloorTransitionZone : NetworkBehaviour
    {
        [Header("Destination (next floor)")]
        [Tooltip("Spawn points at the START of the next floor. Players are distributed " +
                 "round-robin. 1 point works (everyone stacks); 4 is ideal.")]
        [SerializeField] private List<Transform> targetSpawnPoints = new();

        [Header("Timing")]
        [Tooltip("Seconds for the fade-to-black before the teleport fires.")]
        [SerializeField, Min(0f)] private float fadeOutDuration = 0.6f;

        [Tooltip("Seconds held on black AFTER the teleport, before fading back in " +
                 "(lets ClientNetworkTransform + camera settle off-screen).")]
        [SerializeField, Min(0f)] private float blackHoldDuration = 0.5f;

        [SerializeField, Min(0f)] private float fadeInDuration = 0.6f;

        [Header("Behaviour")]
        [Tooltip("TRUE = elevator fires once and stays consumed (standard floor exit). " +
                 "FALSE = re-arms after the transition (debug / backtracking).")]
        [SerializeField] private bool oneShot = true;

        [Tooltip("Server re-checks readiness at this interval. Catches deaths/revives that " +
                 "happen while heroes stand inside (BaseHero has no OnDeath event to hook).")]
        [SerializeField, Min(0.1f)] private float readinessTickInterval = 0.25f;

        // ---- Replicated state (server-write, everyone-read) ----

        /// <summary>TRUE when every ALIVE hero is inside the trigger. Drives the client prompt.</summary>
        private readonly NetworkVariable<bool> _netReady = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Alive-heroes-inside / total-alive, packed for UI ("3/4 players ready").</summary>
        private readonly NetworkVariable<int> _netAliveInside = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _netAliveTotal = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>TRUE once the elevator has fired (or while it is firing) — locks re-entry.</summary>
        private readonly NetworkVariable<bool> _netConsumed = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // ---- Public read API (any peer; for prompt/HUD widgets) ----

        public bool IsReady => _netReady.Value;
        public bool IsConsumed => _netConsumed.Value;
        public int AliveInside => _netAliveInside.Value;
        public int AliveTotal => _netAliveTotal.Value;

        /// <summary>LOCAL-peer event: "Hold E" prompt should show/hide. UI subscribes.</summary>
        public event System.Action<bool> OnPromptStateChanged;

        /// <summary>
        /// STATIC local-peer UI hook (same decoupling as SectionManager.OnSectionChanged):
        /// fires with THIS zone whenever the local hero stands inside it and any replicated
        /// state changes (counts/ready/consumed), and with NULL when they step out.
        /// One HUD widget (TransitionPromptUI) serves every zone in the scene through this —
        /// gameplay code never references UI.
        /// </summary>
        public static event System.Action<FloorTransitionZone> OnLocalZoneChanged;

        // ---- Internals ----

        private readonly HashSet<BaseHero> _heroesInZone = new(); // SERVER truth
        private bool _localHeroInside;                            // CLIENT-side prompt flag
        private bool _promptShown;
        private bool _uiNotified;                                 // this zone currently drives the HUD widget
        private float _nextTickTime;
        private bool _transitionRunning;                          // server re-entrancy guard
        private InputSystem_Actions _input;                       // client-only

        // =====================================================================
        // Lifecycle
        // =====================================================================

        public override void OnNetworkSpawn()
        {
            // Prompt inputs only matter on machines with a local player (host included).
            if (IsClient)
            {
                _input = new InputSystem_Actions();
                _input.Player.Interact.performed += OnInteractPerformed; // "Hold" interaction: fires after E is held
                _input.Player.Interact.Enable();

                _netReady.OnValueChanged += HandleReplicatedStateChanged;
                _netConsumed.OnValueChanged += HandleReplicatedStateChanged;
                _netAliveInside.OnValueChanged += HandleCountChanged;
                _netAliveTotal.OnValueChanged += HandleCountChanged;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (_input != null)
            {
                _input.Player.Interact.performed -= OnInteractPerformed;
                _input.Dispose();
                _input = null;
            }
            _netReady.OnValueChanged -= HandleReplicatedStateChanged;
            _netConsumed.OnValueChanged -= HandleReplicatedStateChanged;
            _netAliveInside.OnValueChanged -= HandleCountChanged;
            _netAliveTotal.OnValueChanged -= HandleCountChanged;

            if (_uiNotified) { _uiNotified = false; OnLocalZoneChanged?.Invoke(null); }
        }

        // =====================================================================
        // Trigger tracking — runs on EVERY peer; server keeps truth, owner keeps prompt
        // =====================================================================

        private void OnTriggerEnter(Collider other)
        {
            BaseHero hero = other.GetComponentInParent<BaseHero>();
            if (hero == null) return;

            if (IsServer) _heroesInZone.Add(hero); // dead heroes enter the SET; readiness filters them
            if (hero.IsOwner) { _localHeroInside = true; RefreshPrompt(); }
        }

        private void OnTriggerExit(Collider other)
        {
            BaseHero hero = other.GetComponentInParent<BaseHero>();
            if (hero == null) return;

            if (IsServer) _heroesInZone.Remove(hero);
            if (hero.IsOwner) { _localHeroInside = false; RefreshPrompt(); }
        }

        // =====================================================================
        // SERVER — readiness evaluation (polled: BaseHero exposes no OnDeath event)
        // =====================================================================

        private void Update()
        {
            if (!IsServer || _netConsumed.Value || Time.time < _nextTickTime) return;
            _nextTickTime = Time.time + readinessTickInterval;
            EvaluateReadiness();
        }

        private void EvaluateReadiness()
        {
            // Prune despawned refs (disconnects); count ALIVE heroes standing inside.
            _heroesInZone.RemoveWhere(h => h == null || !h.IsSpawned);
            int aliveInside = 0;
            foreach (BaseHero hero in _heroesInZone)
                if (hero.IsAlive) aliveInside++;

            // Total alive across ALL connected players — the convergence requirement.
            int aliveTotal = 0;
            foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
            {
                if (client.PlayerObject != null &&
                    client.PlayerObject.TryGetComponent(out BaseHero hero) &&
                    hero.IsAlive) aliveTotal++;
            }

            _netAliveInside.Value = aliveInside;
            _netAliveTotal.Value = aliveTotal;
            _netReady.Value = aliveTotal > 0 && aliveInside >= aliveTotal;
        }

        // =====================================================================
        // CLIENT — prompt + activation request
        // =====================================================================

        private void HandleReplicatedStateChanged(bool previous, bool current) => RefreshPrompt();
        private void HandleCountChanged(int previous, int current) => RefreshPrompt();

        private void RefreshPrompt()
        {
            bool show = _localHeroInside && _netReady.Value && !_netConsumed.Value && LocalHeroIsAlive();
            if (show != _promptShown)
            {
                _promptShown = show;
                OnPromptStateChanged?.Invoke(show);
            }

            // HUD widget notification — only THIS zone may show or clear the widget.
            // (Replicated-state callbacks fire on every zone instance; a zone the local
            // hero is NOT inside must never emit null and stomp another zone's prompt.)
            if (_localHeroInside)
            {
                _uiNotified = true;
                OnLocalZoneChanged?.Invoke(this);
            }
            else if (_uiNotified)
            {
                _uiNotified = false;
                OnLocalZoneChanged?.Invoke(null);
            }
        }

        private void OnInteractPerformed(UnityEngine.InputSystem.InputAction.CallbackContext ctx)
        {
            RefreshPrompt(); // re-derive in case replication landed since last trigger event
            if (!_promptShown) return;
            RequestTransitionRpc();
        }

        private bool LocalHeroIsAlive()
        {
            NetworkObject player = NetworkManager?.LocalClient?.PlayerObject;
            return player != null && player.TryGetComponent(out BaseHero hero) && hero.IsAlive;
        }

        // =====================================================================
        // SERVER — validated execution
        // =====================================================================

        /// <summary>
        /// Client claim: "I'm inside the armed zone and pressed E". Server re-validates
        /// everything — sender must be a connected, ALIVE hero currently in the set.
        /// </summary>
        [Rpc(SendTo.Server, RequireOwnership = false)]
        private void RequestTransitionRpc(RpcParams rpcParams = default)
        {
            if (_transitionRunning || _netConsumed.Value) return;

            EvaluateReadiness(); // fresh check, don't trust the last tick
            if (!_netReady.Value) return;

            // Sender validation: must be an alive hero standing inside the zone.
            ulong senderId = rpcParams.Receive.SenderClientId;
            if (!NetworkManager.ConnectedClients.TryGetValue(senderId, out NetworkClient sender) ||
                sender.PlayerObject == null ||
                !sender.PlayerObject.TryGetComponent(out BaseHero senderHero) ||
                !senderHero.IsAlive || !_heroesInZone.Contains(senderHero))
            {
                Debug.LogWarning($"[FloorTransitionZone] Rejected activation from client {senderId}.", this);
                return;
            }

            StartCoroutine(ServerRunTransition());
        }

        private IEnumerator ServerRunTransition()
        {
            _transitionRunning = true;
            _netConsumed.Value = true; // arms the lock on every peer immediately

            // 1. Everyone fades to black (client-side visual, fire-and-forget).
            FadeOutRpc(fadeOutDuration);
            yield return new WaitForSeconds(fadeOutDuration);

            // 2. Teleport EVERY player — dead ones included, staying dead (GDD floor rule;
            //    revival is SectionManager's job at section boundaries, not ours).
            List<Transform> points = targetSpawnPoints;
            int slot = 0;
            foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
            {
                if (client.PlayerObject == null) continue;

                Vector3 pos = points.Count > 0
                    ? points[slot++ % points.Count].position
                    : transform.position;

                TeleportLocalHeroRpc(pos, RpcTarget.Single(client.ClientId, RpcTargetUse.Temp));
            }

            // Heroes left the trigger via teleport — OnTriggerExit is NOT guaranteed to
            // fire for warped colliders, so reset the server set manually.
            _heroesInZone.Clear();

            // 3. Hold black while transforms replicate + camera snaps, then fade back.
            yield return new WaitForSeconds(blackHoldDuration);
            FadeInRpc(fadeInDuration);

            if (!oneShot)
            {
                yield return new WaitForSeconds(fadeInDuration);
                _netConsumed.Value = false; // re-arm
            }
            _transitionRunning = false;
        }

        // =====================================================================
        // RPCs — visuals to everyone, teleport to the OWNER (ClientNetworkTransform)
        // =====================================================================

        [Rpc(SendTo.Everyone)]
        private void FadeOutRpc(float duration) => ScreenFadeController.RequestFadeOut(duration);

        [Rpc(SendTo.Everyone)]
        private void FadeInRpc(float duration) => ScreenFadeController.RequestFadeIn(duration);

        /// <summary>
        /// Runs ON THE OWNING CLIENT (same pattern as SectionManager.TeleportLocalHeroRpc).
        /// CharacterController caches its internal position — toggling it around the warp
        /// prevents it from snapping the hero back on its next Move().
        /// </summary>
        [Rpc(SendTo.SpecifiedInParams)]
        private void TeleportLocalHeroRpc(Vector3 position, RpcParams rpcParams = default)
        {
            NetworkObject player = NetworkManager.LocalClient?.PlayerObject;
            if (player == null) return;

            CharacterController cc = player.GetComponent<CharacterController>();
            bool wasEnabled = cc != null && cc.enabled;
            if (wasEnabled) cc.enabled = false;

            player.transform.position = position;
            Physics.SyncTransforms(); // flush so the new position wins this physics step

            if (wasEnabled) cc.enabled = true;
        }

        // =====================================================================
        // Editor QoL
        // =====================================================================

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.6f);
            if (TryGetComponent(out BoxCollider box))
                Gizmos.DrawWireCube(transform.TransformPoint(box.center),
                                    Vector3.Scale(box.size, transform.lossyScale));

            Gizmos.color = Color.green;
            foreach (Transform t in targetSpawnPoints)
            {
                if (t == null) continue;
                Gizmos.DrawLine(transform.position, t.position);
                Gizmos.DrawWireSphere(t.position, 0.4f);
            }
        }
    }
}
