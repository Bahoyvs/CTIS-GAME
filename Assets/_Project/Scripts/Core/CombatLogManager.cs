using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Core
{
    /// <summary>
    /// Centralized action logger — the network-aware equivalent of a structured logging
    /// service. All gameplay actions funnel through here so one breakpoint / one UI console
    /// sees everything.
    ///
    /// FLOW: authoritative actions are logged ON THE SERVER, which prints locally and
    /// broadcasts one ClientRpc so every player's console shows the same combat history.
    /// Client-local actions (e.g. a roll, which is owner-authoritative movement) log
    /// locally only — no traffic for things the server doesn't arbitrate.
    ///
    /// Output format: "[Server] Player_2 (Kerem) casted Skill_1 at X:10.0, Y:5.0"
    /// (Y in the log = world Z, since gameplay happens on the XZ ground plane.)
    ///
    /// SETUP: scene GameObject with CombatLogManager + NetworkObject (so the ClientRpc works).
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class CombatLogManager : NetworkBehaviour
    {
        public static CombatLogManager Instance { get; private set; }

        /// <summary>Formatted entries, already prefixed. Drive an on-screen debug console from this.</summary>
        public static event Action<string> OnEntryLogged;

        [SerializeField] private bool mirrorToUnityConsole = true;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy(); // NetworkBehaviour.OnDestroy is virtual and must be chained.
        }

        // ---------------------------------------------------------------- Static API

        /// <summary>
        /// Log an authoritative action. Call from SERVER-side code (RPC handlers, AI ticks).
        /// Falls back to a local-only log if called on a client, so it is always safe to call.
        /// </summary>
        public static void LogAction(string actorName, string verb, string detail, Vector3 worldPos)
        {
            string msg = Format(actorName, verb, detail, worldPos);

            if (Instance == null || !Instance.IsSpawned)
            {
                Debug.Log($"[Offline] {msg}");
                return;
            }

            Instance.Print(msg);
            if (Instance.IsServer)
                Instance.BroadcastLogClientRpc(new FixedString128Bytes(Truncate(msg, 125)));
        }

        /// <summary>Log a purely local/owner-side action (roll, input feedback). Never networked.</summary>
        public static void LogLocal(string actorName, string verb, string detail, Vector3 worldPos)
        {
            string msg = Format(actorName, verb, detail, worldPos);
            if (Instance != null) Instance.Print(msg);
            else Debug.Log($"[Offline] {msg}");
        }

        /// <summary>
        /// Log a status effect application. Call from SERVER-side code.
        /// </summary>
        public static void LogEffect(string actorName, string effectName, Vector3 worldPos)
        {
            LogAction(actorName, "got effect", effectName, worldPos);
        }

        /// <summary>
        /// Log ability activation. Call from SERVER-side code (AbilityController.ServerTryActivate).
        /// Includes ability name, mode, and caster position.
        /// </summary>
        public static void LogAbilityActivated(string actorName, string abilityName, string mode, Vector3 worldPos)
        {
            LogAction(actorName, "activated", $"{abilityName} ({mode})", worldPos);
        }

        /// <summary>
        /// Log ability cooldown starting. Call from SERVER-side code.
        /// Shows ability name and cooldown duration.
        /// </summary>
        public static void LogAbilityCooldown(string actorName, string abilityName, float cooldown)
        {
            string msg = Format(actorName, "started cooldown on", $"{abilityName} ({cooldown:F1}s)", Vector3.zero);
            if (Instance == null || !Instance.IsSpawned)
            {
                Debug.Log($"[Offline] {msg}");
                return;
            }

            Instance.Print(msg);
            if (Instance.IsServer)
                Instance.BroadcastLogClientRpc(new FixedString128Bytes(Truncate(msg, 125)));
        }

        /// <summary>
        /// Log ability channel start. Call from SERVER-side code.
        /// Shows ability name and channel duration.
        /// </summary>
        public static void LogAbilityChannelStart(string actorName, string abilityName, float duration, Vector3 worldPos)
        {
            LogAction(actorName, "started channeling", $"{abilityName} ({duration:F1}s)", worldPos);
        }

        /// <summary>
        /// Log ability channel end. Call from SERVER-side code.
        /// Shows whether channel completed or was interrupted.
        /// </summary>
        public static void LogAbilityChannelEnd(string actorName, string abilityName, bool completed, Vector3 worldPos)
        {
            string result = completed ? "completed" : "interrupted";
            LogAction(actorName, $"channel {result}", abilityName, worldPos);
        }

        /// <summary>
        /// Log toggle ability state change. Call from SERVER-side code.
        /// Shows toggle ON or OFF status.
        /// </summary>
        public static void LogAbilityToggle(string actorName, string abilityName, bool isOn, Vector3 worldPos)
        {
            string state = isOn ? "toggled ON" : "toggled OFF";
            LogAction(actorName, state, abilityName, worldPos);
        }

        /// <summary>
        /// Log ability failure (gating check failed). Call from SERVER-side code.
        /// Shows reason (silenced, cooldown, charging, etc.).
        /// </summary>
        public static void LogAbilityBlocked(string actorName, string abilityName, string reason)
        {
            string msg = Format(actorName, "failed to use", $"{abilityName} ({reason})", Vector3.zero);
            if (Instance == null || !Instance.IsSpawned)
            {
                Debug.Log($"[Offline] {msg}");
                return;
            }

            Instance.Print(msg);
            if (Instance.IsServer)
                Instance.BroadcastLogClientRpc(new FixedString128Bytes(Truncate(msg, 125)));
        }

        private static string Format(string actorName, string verb, string detail, Vector3 pos)
            => $"{actorName} {verb} {detail} at X:{pos.x:F1}, Y:{pos.z:F1}";

        private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);

        // ---------------------------------------------------------------- Networking

        /// <summary>
        /// FixedString128Bytes instead of string: NGO serializes it without GC allocation and
        /// with a hard size cap — a runaway log line can't balloon a packet.
        /// </summary>
        [ClientRpc]
        private void BroadcastLogClientRpc(FixedString128Bytes message)
        {
            // The host is server AND client — it already printed in LogAction. Skip the dupe.
            if (IsServer) return;
            Print(message.ToString());
        }

        private void Print(string message)
        {
            string prefixed = $"[{(IsServer ? "Server" : "Client")}] {message}";
            if (mirrorToUnityConsole) Debug.Log(prefixed);
            OnEntryLogged?.Invoke(prefixed);
        }
    }
}
