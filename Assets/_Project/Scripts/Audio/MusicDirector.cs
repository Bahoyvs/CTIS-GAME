using System;
using UnityEngine;
using UnityEngine.Audio;
using CBuilding.Core;

namespace CBuilding.Audio
{
    /// <summary>High-level musical state. Explicit — never inferred from AudioSources.</summary>
    public enum MusicState { Explore, TransitioningToCombat, Combat, TransitioningToExplore }

    /// <summary>
    /// Horizontal-sequencing (branching) music system. Client-local ONLY — like
    /// CameraModeController (GS-15), each client runs its own MusicDirector against its
    /// own perception of threat. There is NO network sync of playback position; if
    /// beat-identical audio across all 4 clients ever becomes a hard requirement, that
    /// is a separate problem (RPC dspTime negotiation + latency compensation) and must
    /// NOT be bolted onto this class.
    ///
    /// Core model
    /// ----------
    /// Music is a chain of SEGMENTS. A segment = one clip (ExploreLoop / CombatTransition
    /// / CombatLoop / CombatEnd) scheduled at an exact dspTime on one of two ping-ponged
    /// AudioSources via PlayScheduled. Loops are NOT AudioSource.loop — every loop
    /// iteration is its own scheduled segment, so we always know the exact dspTime of
    /// the current segment's end and of every bar boundary inside it.
    ///
    /// At most ONE segment is queued ahead at any time. The Update tick:
    ///   1. Commits the queued segment once its start dspTime passes (state flips there,
    ///      i.e. exactly when the change becomes audible).
    ///   2. If nothing is queued, decides what to schedule next:
    ///      - loop state + change wanted (threat flip or biome swap) → branch clip at the
    ///        NEXT BAR BOUNDARY (computed from the segment's start anchor, never "now");
    ///      - loop state, no change → same loop again at segment end;
    ///      - stinger near its end → auto-chain its follow-up loop at segment end.
    ///
    /// Pending-request latching (edge case, per spec)
    /// ----------------------------------------------
    /// Threat events only write _threatHigh (the DESIRED state). Once a segment is
    /// queued it is immutable — we never cancel or interrupt a scheduled/playing
    /// stinger. If threat flips back mid-stinger (e.g. rises again during
    /// TransitioningToExplore), nothing happens until the stinger auto-chains into its
    /// loop; the next tick then sees desired != current and schedules the opposite
    /// transition at that loop's first bar boundary. Net effect: you always hear at
    /// least ~1 bar of the loop between two stingers, which is also the musically
    /// correct behaviour.
    ///
    /// Biome swap policy (Requirement 5, option (a) chosen)
    /// ----------------------------------------------------
    /// RequestClipSetSwap latches the new set; at the next bar boundary (or at stinger
    /// completion) we hard-cut into the new set's ExploreLoop. Chosen over crossfading
    /// because both sources are already owned by the sequencer and a bar-aligned cut is
    /// artifact-free by construction. ForceStopAndSwap exists for death/section-reset
    /// where an instant cut is acceptable.
    /// </summary>
    [DisallowMultipleComponent]
    public class MusicDirector : MonoBehaviour
    {
        public static MusicDirector Instance { get; private set; }

        // ------------------------------------------------------------------ Inspector

        [Header("Clip sets")]
        [Tooltip("Optional: index 0 → Section 1, index 1 → Section 2, ... Subscribed to " +
                 "SectionManager.OnSectionChanged; a section change requests a bar-aligned " +
                 "swap to the matching set. Leave empty to drive swaps manually via " +
                 "RequestClipSetSwap().")]
        [SerializeField] private MusicClipSetSO[] clipSetsBySection;

        [Tooltip("Set played on Start() when 'Auto Start' is on and no section mapping " +
                 "applies. Also the fallback if a section index has no entry above.")]
        [SerializeField] private MusicClipSetSO initialClipSet;

        [SerializeField] private bool autoStart = true;

        [Header("Threat input")]
        [Tooltip("Any component implementing IThreatStateProvider (e.g. ThreatStateRelay). " +
                 "Optional — can also be injected via SetThreatProvider().")]
        [SerializeField] private MonoBehaviour threatProviderBehaviour;

        [Header("Scheduling")]
        [Tooltip("PlayScheduled must target a dspTime at least this far in the future. " +
                 "0.1–0.2 s is safe; below ~0.05 s risks late scheduling on hitchy frames.")]
        [Range(0.05f, 0.5f)] [SerializeField] private float scheduleAheadTime = 0.15f;

        [Header("Output")]
        [SerializeField] private AudioMixerGroup outputGroup;
        [Range(0f, 1f)] [SerializeField] private float volume = 1f;

        [Header("Debug / QA")]
        [Tooltip("On-screen overlay: state, biome, bar count, next event dspTime. " +
                 "OnGUI allocates — QA builds only, leave OFF for profiling.")]
        [SerializeField] private bool showDebugOverlay;

        // ------------------------------------------------------------------ Runtime state

        /// <summary>One scheduled slice of music. Immutable once queued.</summary>
        private struct Segment
        {
            public MusicClipSetSO Set;
            public MusicRole Role;
            public MusicState State;     // state that becomes current when this segment commits
            public double StartDsp;      // dspTime of the musical downbeat
            public double EndDsp;        // StartDsp + musical duration (whole bars)
            public int SourceIndex;      // which of the two sources plays it
        }

        private readonly AudioSource[] _sources = new AudioSource[2];

        private MusicClipSetSO _activeSet;        // set the CURRENT segment belongs to
        private Segment _current;                 // segment currently audible
        private Segment _queued;                  // next scheduled segment (if _hasQueued)
        private bool _hasQueued;
        private bool _playing;

        private MusicState _state = MusicState.Explore;

        // Desired inputs (latched; consumed only at decision points — see class docs).
        private bool _threatHigh;                 // desired threat state
        private MusicClipSetSO _pendingSet;       // desired biome swap, null if none

        private IThreatStateProvider _threatProvider;
        private bool _resumeResetPending;         // set by OnApplicationPause(false)

        // ------------------------------------------------------------------ Public / debug surface

        public MusicState CurrentState => _state;
        public string CurrentBiomeName => _activeSet != null ? _activeSet.biomeName : "-";

        /// <summary>0-based bar count since the current segment's downbeat (clamped ≥ 0).</summary>
        public int CurrentBarIndex
        {
            get
            {
                if (!_playing || _activeSet == null) return 0;
                double bars = (AudioSettings.dspTime - _current.StartDsp) / _current.Set.SecondsPerBar;
                return bars > 0.0 ? (int)bars : 0;
            }
        }

        /// <summary>dspTime of the next committed musical event: queued segment start, else current segment end.</summary>
        public double NextScheduledEventDsp => _hasQueued ? _queued.StartDsp : _current.EndDsp;

        /// <summary>QA: force desired threat high, bypassing the provider (next provider event overrides).</summary>
        public void DebugForceCombat() => _threatHigh = true;

        /// <summary>QA: force desired threat low, bypassing the provider (next provider event overrides).</summary>
        public void DebugForceExplore() => _threatHigh = false;

        // ------------------------------------------------------------------ Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[MusicDirector] Duplicate instance destroyed.", this);
                Destroy(gameObject);
                return;
            }
            Instance = this;

            for (int i = 0; i < 2; i++)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;                 // deliberate — every play is PlayScheduled
                src.spatialBlend = 0f;            // 2D music
                src.outputAudioMixerGroup = outputGroup;
                src.volume = volume;
                _sources[i] = src;
            }

            if (threatProviderBehaviour != null)
            {
                if (threatProviderBehaviour is IThreatStateProvider p) SetThreatProvider(p);
                else Debug.LogError("[MusicDirector] Assigned threat provider does not implement IThreatStateProvider.", this);
            }
        }

        private void OnEnable()
        {
            if (clipSetsBySection != null && clipSetsBySection.Length > 0)
                SectionManager.OnSectionChanged += HandleSectionChanged;
        }

        private void OnDisable()
        {
            if (clipSetsBySection != null && clipSetsBySection.Length > 0)
                SectionManager.OnSectionChanged -= HandleSectionChanged;
        }

        private void Start()
        {
            if (!autoStart) return;
            MusicClipSetSO set = ResolveSectionSet(SectionManager.CurrentSection) ?? initialClipSet;
            if (set != null) StartMusic(set);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_threatProvider != null) _threatProvider.OnThreatStateChanged -= HandleThreatChanged;
        }

        // ------------------------------------------------------------------ Public API

        /// <summary>Swap/inject the threat provider at runtime (e.g. after network spawn).</summary>
        public void SetThreatProvider(IThreatStateProvider provider)
        {
            if (_threatProvider != null) _threatProvider.OnThreatStateChanged -= HandleThreatChanged;
            _threatProvider = provider;
            if (_threatProvider == null) return;
            _threatProvider.OnThreatStateChanged += HandleThreatChanged;
            _threatHigh = _threatProvider.IsHighThreat; // read initial state, don't wait for an event
        }

        /// <summary>Begin playback from silence: ExploreLoop of the given set, bar 0.</summary>
        public void StartMusic(MusicClipSetSO set)
        {
            if (set == null || !set.IsValid)
            {
                Debug.LogError("[MusicDirector] StartMusic called with a null/incomplete clip set.", this);
                return;
            }

            StopAllSources();
            _activeSet = set;
            _pendingSet = null;
            _hasQueued = false;
            _state = MusicState.Explore;
            _playing = true;

            // First segment: anchor a little further out than the normal lookahead so
            // the very first PlayScheduled is never late on a loading-hitch frame.
            double start = AudioSettings.dspTime + Math.Max(scheduleAheadTime * 2f, 0.25f);
            _current = MakeSegment(set, MusicRole.ExploreLoop, MusicState.Explore, start, 0);
            ScheduleSource(_current);
        }

        /// <summary>
        /// SAFE biome swap (policy (a)): latches the new set; the sequencer hard-cuts into
        /// its ExploreLoop at the next bar boundary (or when the in-flight stinger ends).
        /// No immediate audio interruption, no crossfade needed — the cut is bar-aligned.
        /// </summary>
        public void RequestClipSetSwap(MusicClipSetSO newSet)
        {
            if (newSet == null || !newSet.IsValid)
            {
                Debug.LogError("[MusicDirector] RequestClipSetSwap: null/incomplete clip set ignored.", this);
                return;
            }
            if (newSet == _activeSet && _pendingSet == null) return; // already there
            if (!_playing) { StartMusic(newSet); return; }
            _pendingSet = newSet;
        }

        /// <summary>
        /// HARD swap for death / section-reset / menu exit: stops both sources NOW and
        /// restarts on the new set's ExploreLoop. Audible cut is acceptable by contract.
        /// </summary>
        public void ForceStopAndSwap(MusicClipSetSO newSet)
        {
            StopAllSources();
            _playing = false;
            if (newSet != null) StartMusic(newSet);
        }

        /// <summary>Stop all music immediately (hard cut).</summary>
        public void StopMusic()
        {
            StopAllSources();
            _playing = false;
            _hasQueued = false;
            _pendingSet = null;
        }

        // ------------------------------------------------------------------ Input handlers

        // Latch only. NEVER schedules directly — scheduling "as soon as threat changes"
        // would land off-beat. Update() consumes this at the next decision point.
        private void HandleThreatChanged(bool high) => _threatHigh = high;

        private void HandleSectionChanged(int section)
        {
            MusicClipSetSO set = ResolveSectionSet(section);
            if (set != null) RequestClipSetSwap(set);
        }

        private MusicClipSetSO ResolveSectionSet(int section)
        {
            int idx = section - 1; // sections are 1-based
            if (clipSetsBySection == null || idx < 0 || idx >= clipSetsBySection.Length) return null;
            return clipSetsBySection[idx];
        }

        // ------------------------------------------------------------------ Tick

        // Hot path: comparisons + at most one PlayScheduled. Zero allocation, no strings,
        // no LINQ, no Find*. Cheap enough for Update(); a lower-frequency coroutine would
        // also work but WaitForSeconds granularity vs. lookahead margin buys nothing here.
        private void Update()
        {
            if (!_playing) return;

            double now = AudioSettings.dspTime;

            // -------- Pause/focus recovery (Requirement 8).
            // After OnApplicationPause(false), dspTime may have jumped (platform-dependent:
            // it can freeze during pause) leaving every anchor in the past. Never try to
            // "catch up" missed boundaries — hard re-anchor into the current state's loop.
            if (_resumeResetPending)
            {
                _resumeResetPending = false;
                ReanchorIntoCurrentLoop(now);
                return;
            }

            // -------- 1. Commit the queued segment once its downbeat has passed.
            // State flips HERE — exactly when the change becomes audible — so the debug
            // state always matches what the player hears.
            if (_hasQueued && now >= _queued.StartDsp)
            {
                _current = _queued;
                _state = _queued.State;
                _activeSet = _queued.Set;
                _hasQueued = false;
            }

            // A queued segment is immutable — nothing to decide until it commits.
            if (_hasQueued) return;

            // -------- Defensive: if a huge hitch made us miss scheduling entirely and the
            // current segment already ended (silence!), re-anchor instead of scheduling
            // into the past (PlayScheduled with a past dspTime fires immediately, off-beat).
            if (now > _current.EndDsp)
            {
                ReanchorIntoCurrentLoop(now);
                return;
            }

            // -------- 2. Decide the next segment.
            switch (_state)
            {
                case MusicState.Explore:
                case MusicState.Combat:
                    TickLoopState(now);
                    break;

                case MusicState.TransitioningToCombat:
                case MusicState.TransitioningToExplore:
                    TickStingerState(now);
                    break;
            }
        }

        /// <summary>
        /// Loop states can branch at ANY bar boundary, not just at loop end.
        /// Priority: biome swap > threat transition > plain loop continuation.
        /// </summary>
        private void TickLoopState(double now)
        {
            bool inCombat = _state == MusicState.Combat;
            bool wantsBranch = _pendingSet != null || _threatHigh != inCombat;

            if (wantsBranch)
            {
                // BAR-BOUNDARY MATH (Requirement 2):
                // Boundaries live on the grid  StartDsp + k * secondsPerBar  (k = 1,2,...),
                // anchored to the dspTime the CURRENT clip's downbeat actually played —
                // never to "now" — so alignment cannot drift over a long session.
                // We need the first boundary that is still schedulable, i.e. at least
                // scheduleAheadTime in the future (Requirement 3).
                double bar = _current.Set.SecondsPerBar;
                double elapsed = (now + scheduleAheadTime) - _current.StartDsp;
                long k = (long)Math.Ceiling(elapsed / bar);
                if (k < 1) k = 1;
                double boundary = _current.StartDsp + k * bar;

                // Loop end IS a bar boundary (durations are whole bars); never branch past it.
                if (boundary > _current.EndDsp) boundary = _current.EndDsp;

                // Schedule only when the boundary enters the lookahead window. Scheduling
                // earlier would needlessly freeze the decision (a threat flip-back could
                // otherwise still cancel the branch by simply never being scheduled).
                if (boundary - now > scheduleAheadTime * 2f) return;

                Segment next;
                if (_pendingSet != null)
                {
                    // Biome swap, policy (a): bar-aligned hard cut into new set's ExploreLoop.
                    next = MakeSegment(_pendingSet, MusicRole.ExploreLoop, MusicState.Explore,
                                       boundary, 1 - _current.SourceIndex);
                    _pendingSet = null; // committed
                }
                else if (inCombat)
                {
                    next = MakeSegment(_activeSet, MusicRole.CombatEnd, MusicState.TransitioningToExplore,
                                       boundary, 1 - _current.SourceIndex);
                }
                else
                {
                    next = MakeSegment(_activeSet, MusicRole.CombatTransition, MusicState.TransitioningToCombat,
                                       boundary, 1 - _current.SourceIndex);
                }

                // Sample-accurate cut of the running loop at the same boundary the new
                // segment starts — this is what makes the branch artifact-free.
                _sources[_current.SourceIndex].SetScheduledEndTime(boundary);
                ScheduleSource(next);
                _queued = next;
                _hasQueued = true;
                return;
            }

            // No change wanted → keep looping: schedule the SAME clip at segment end on the
            // other source (ping-pong). We do NOT use AudioSource.loop because we need the
            // exact dspTime of every iteration's end to schedule whatever comes after it.
            if (_current.EndDsp - now <= scheduleAheadTime * 2f)
            {
                Segment next = MakeSegment(_activeSet, _current.Role, _state,
                                           _current.EndDsp, 1 - _current.SourceIndex);
                ScheduleSource(next);
                _queued = next;
                _hasQueued = true;
            }
        }

        /// <summary>
        /// Stingers play exactly once and auto-chain at their end. Threat flips that
        /// arrived mid-stinger were only latched into _threatHigh; the chained loop's
        /// first TickLoopState will notice desired != current and branch at its first
        /// bar boundary. (Pending-request resolution point — see class docs.)
        /// </summary>
        private void TickStingerState(double now)
        {
            if (_current.EndDsp - now > scheduleAheadTime * 2f) return;

            Segment next;
            if (_pendingSet != null)
            {
                // Biome swap requested mid-stinger: honor it at the stinger's natural end.
                next = MakeSegment(_pendingSet, MusicRole.ExploreLoop, MusicState.Explore,
                                   _current.EndDsp, 1 - _current.SourceIndex);
                _pendingSet = null;
            }
            else if (_state == MusicState.TransitioningToCombat)
            {
                next = MakeSegment(_activeSet, MusicRole.CombatLoop, MusicState.Combat,
                                   _current.EndDsp, 1 - _current.SourceIndex);
            }
            else
            {
                next = MakeSegment(_activeSet, MusicRole.ExploreLoop, MusicState.Explore,
                                   _current.EndDsp, 1 - _current.SourceIndex);
            }

            ScheduleSource(next);
            _queued = next;
            _hasQueued = true;
        }

        // ------------------------------------------------------------------ Scheduling internals

        private Segment MakeSegment(MusicClipSetSO set, MusicRole role, MusicState state,
                                    double startDsp, int sourceIndex)
        {
            return new Segment
            {
                Set = set,
                Role = role,
                State = state,
                StartDsp = startDsp,
                EndDsp = startDsp + set.GetMusicalDuration(role),
                SourceIndex = sourceIndex,
            };
        }

        /// <summary>
        /// Points a source at the segment's clip and PlayScheduled()s it so the MUSICAL
        /// DOWNBEAT — not the file start — lands on StartDsp:
        /// we pre-seek the source by the clip's LeadInOffsetSeconds (Requirement 4), so
        /// export padding/transient bleed before the downbeat is skipped, not shifted.
        /// </summary>
        private void ScheduleSource(in Segment seg)
        {
            AudioClip clip = seg.Set.GetClip(seg.Role);
            AudioSource src = _sources[seg.SourceIndex];

            src.Stop();                                  // clears any stale schedule on this source
            src.clip = clip;
            src.volume = volume;

            float leadIn = seg.Set.GetLeadIn(seg.Role);
            src.timeSamples = leadIn > 0f
                ? (int)Math.Round(leadIn * clip.frequency)
                : 0;

            src.PlayScheduled(seg.StartDsp);
        }

        private void StopAllSources()
        {
            for (int i = 0; i < 2; i++)
                if (_sources[i] != null) _sources[i].Stop();
        }

        /// <summary>
        /// Clean restart into the loop that matches the current musical direction
        /// (mid-stinger → that stinger's destination loop). Used after pause-resume and
        /// as the missed-schedule fallback. Bar count restarts at 0 by design — we never
        /// chase boundaries that elapsed while dspTime was frozen or we weren't ticking.
        /// </summary>
        private void ReanchorIntoCurrentLoop(double now)
        {
            StopAllSources();
            _hasQueued = false;

            MusicState loopState =
                (_state == MusicState.Combat || _state == MusicState.TransitioningToCombat)
                    ? MusicState.Combat
                    : MusicState.Explore;
            MusicRole role = loopState == MusicState.Combat ? MusicRole.CombatLoop
                                                            : MusicRole.ExploreLoop;

            MusicClipSetSO set = _pendingSet != null ? _pendingSet : _activeSet;
            if (_pendingSet != null) { loopState = MusicState.Explore; role = MusicRole.ExploreLoop; _pendingSet = null; }
            if (set == null) { _playing = false; return; }

            double start = now + Math.Max(scheduleAheadTime * 2f, 0.25f);
            _current = MakeSegment(set, role, loopState, start, 0);
            _state = loopState;
            _activeSet = set;
            ScheduleSource(_current);
        }

        private void OnApplicationPause(bool paused)
        {
            // Requirement 8: on resume, dspTime deltas are platform-dependent (often the
            // clock froze while the app was suspended). All anchors are stale — flag a
            // re-anchor; Update() performs it with a fresh dspTime on the first frame back.
            if (!paused && _playing) _resumeResetPending = true;
        }

        // ------------------------------------------------------------------ Debug overlay (QA only)

        private void OnGUI()
        {
            if (!showDebugOverlay) return;
            // NOTE: allocates (string concat) — acceptable for a QA-only toggle.
            GUILayout.BeginArea(new Rect(10, 10, 420, 120), GUI.skin.box);
            GUILayout.Label($"MusicDirector  [{CurrentBiomeName}]");
            GUILayout.Label($"State: {_state}   DesiredThreat: {(_threatHigh ? "HIGH" : "low")}   PendingSwap: {(_pendingSet != null ? _pendingSet.biomeName : "-")}");
            GUILayout.Label($"Bar: {CurrentBarIndex}   dspNow: {AudioSettings.dspTime:F3}   NextEvent: {NextScheduledEventDsp:F3}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Force Combat")) DebugForceCombat();
            if (GUILayout.Button("Force Explore")) DebugForceExplore();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
