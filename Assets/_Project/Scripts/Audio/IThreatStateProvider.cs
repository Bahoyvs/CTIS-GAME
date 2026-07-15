using System;

namespace CBuilding.Audio
{
    /// <summary>
    /// Minimal input contract for MusicDirector's threat state. Keeps the music system
    /// decoupled from SpawnDirector's internals — MusicDirector never references the
    /// spawning assembly, it only consumes this interface.
    ///
    /// IMPORTANT (per-client isolation): SpawnDirector is a SERVER-ONLY brain, but
    /// MusicDirector runs on every client. The provider therefore must be something
    /// every peer can evaluate locally — e.g. a small NetworkBehaviour whose
    /// server-written NetworkVariable&lt;bool&gt; mirrors the director's threat level,
    /// or a purely local heuristic (enemies aggroed on this client's screen).
    ///
    /// Example — a thin replication shim the SpawnDirector feeds on the server:
    /// <code>
    /// public class ThreatStateRelay : NetworkBehaviour, IThreatStateProvider
    /// {
    ///     private readonly NetworkVariable&lt;bool&gt; _high = new(
    ///         false, NetworkVariableReadPermission.Everyone,
    ///         NetworkVariableWritePermission.Server);
    ///
    ///     public bool IsHighThreat => _high.Value;
    ///     public event Action&lt;bool&gt; OnThreatStateChanged;
    ///
    ///     public override void OnNetworkSpawn() =>
    ///         _high.OnValueChanged += (_, v) => OnThreatStateChanged?.Invoke(v);
    ///
    ///     // Called by SpawnDirector (server) wherever it already tracks engagement,
    ///     // e.g. when _threatByEnemy.Count crosses 0, or UsedThreat crosses a budget
    ///     // threshold with a little hysteresis so the music doesn't flap:
    ///     public void ServerSetHighThreat(bool v) { if (IsServer) _high.Value = v; }
    /// }
    /// </code>
    /// </summary>
    public interface IThreatStateProvider
    {
        /// <summary>Current threat level. Read once on subscribe for initial state.</summary>
        bool IsHighThreat { get; }

        /// <summary>Fired whenever the threat level flips. Payload: new IsHighThreat.</summary>
        event Action<bool> OnThreatStateChanged;
    }
}
