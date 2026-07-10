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
