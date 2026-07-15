# MusicDirector — Horizontal Sequencing Music System

Client-local branching music: `Explore_Loop` ⇄ (`Combat_Transition` / `Combat_End`) ⇄ `Combat_Loop`,
with every clip start driven by `AudioSettings.dspTime` + `PlayScheduled` on a dual-AudioSource
ping-pong. All branches land on bar boundaries computed from the playing clip's start anchor,
so alignment never drifts.

Files: `MusicClipSetSO.cs`, `IThreatStateProvider.cs`, `MusicDirector.cs` (this folder).

---

## 1. Creating a MusicClipSetSO (one per biome × 5)

`Create → CBuilding → Audio → Music Clip Set`, e.g. `MCS_Forest`, under `Assets/_Project/Data/Audio/`.

Per asset:

| Field | What to enter |
|---|---|
| Biome Name | Display name for the debug overlay ("Forest", "Void", ...). |
| Explore Loop / Combat Transition / Combat Loop / Combat End | The 4 clips. **All at the same BPM**, trimmed to whole bars. Stingers are one-shots; loops must be seamless at whole-bar length. |
| BPM | Exactly the DAW project tempo. A wrong BPM is the #1 cause of off-beat branches. |
| Beats Per Bar | Time signature numerator (4 for 4/4, 3 for 3/4). Not hardcoded — Frozen can be in 3/4. |
| *_Lead In (seconds) | Silence/transient bleed at the **start** of that clip's file, before the musical downbeat. Scheduler pre-seeks past it so the downbeat lands exactly on the scheduled dspTime. `0` for clean exports. |
| Quantize Duration To Whole Bars | Leave **ON**. Snaps each clip's musical duration to the nearest whole-bar count so a few ms of export padding can't accumulate into drift. |

The SO's `OnValidate` logs a warning if `(clip length − lead-in)` is more than ~20 ms off a
whole-bar grid at the given BPM — that means either the BPM field is wrong or the clip needs a
lead-in value.

**Clip import settings:** music clips should be `Streaming` or `Compressed In Memory`, and
**Load In Background OFF** for the 4 active-biome clips (a clip still loading when
`PlayScheduled` fires plays late). If clip sets come through Addressables, make sure the set is
fully loaded before calling `RequestClipSetSwap`.

## 2. Scene setup

1. One `MusicDirector` on a client-local GameObject (no `NetworkObject`), one per gameplay scene.
   It creates its own two AudioSources — don't add any.
2. Inspector:
   - **Clip Sets By Section** — element 0 → Section 1, element 1 → Section 2, ... It subscribes to
     `SectionManager.OnSectionChanged` and requests a bar-aligned swap automatically. Leave empty
     to drive swaps manually via `MusicDirector.Instance.RequestClipSetSwap(set)`.
   - **Initial Clip Set** — fallback/startup set when no section mapping applies.
   - **Auto Start** — ON: plays the resolved set's `Explore_Loop` on `Start()`.
   - **Threat Provider Behaviour** — any component implementing `IThreatStateProvider`
     (see `ThreatStateRelay` example in `IThreatStateProvider.cs`). Can also be injected at runtime
     via `SetThreatProvider()` — useful if the provider only exists after network spawn.
   - **Schedule Ahead Time** — 0.15 s default. Raise toward 0.2 s if QA reports late/immediate
     (off-beat) starts on min-spec machines; lower values only tighten worst-case branch latency
     slightly.
   - **Output Group** — route to the Music group of the main mixer.
3. Death / section reset with acceptable hard cut: call `ForceStopAndSwap(newSet)`.
   Normal biome flow: `RequestClipSetSwap(newSet)` (bar-aligned, artifact-free).

## 3. Verifying bar alignment by ear

Turn on **Show Debug Overlay** and use the Force Combat / Force Explore buttons.

- **Correct:** the transition stinger enters exactly on a downbeat of the explore loop — it should
  feel like the band "picked up" the change. The combat loop then enters seamlessly off the
  stinger's last bar.
- **Stinger enters late / "flams" against the loop's downbeat (double-attack):** the stinger clip
  has leading silence or transient bleed → increase that clip's **Lead In** by 5–20 ms steps until
  the attacks line up. (You are compensating the *incoming* clip, not the one playing.)
- **Branch lands consistently between beats:** the SO's **BPM** (or Beats Per Bar) doesn't match
  the audio. Fix the data, not the offsets.
- **Alignment good at first but degrades after many loops:** duration quantize is OFF or the loop
  clip isn't a whole-bar length — re-export or turn **Quantize Duration To Whole Bars** back on.
- **Click/pop at the cut point of a branch:** the *outgoing* loop is being truncated mid-waveform;
  that's expected to be inaudible when music is authored so bars start on strong attacks. If a
  biome's loop has long pads, ask audio to author bar starts on zero-crossings or accept the
  stinger masking it.

## 4. Behaviour notes (by design — don't "fix")

- **Threat flip during a stinger is latched, never interrupts.** Rising threat during
  `TransitioningToExplore` waits for `Combat_End` to finish, plays ≥ ~1 bar of `Explore_Loop`,
  then branches back to combat. Guarantees stingers always complete musically.
- **State flips when the change becomes audible**, not when it's decided — the overlay state
  always matches what you hear.
- **Pause/resume re-anchors from scratch** (current state's loop restarts at bar 0). We never
  chase bar boundaries that "elapsed" while dspTime was frozen.
- **Biome swap policy:** option (a) — bar-aligned hard cut into the new set's `Explore_Loop` —
  chosen over crossfading because both sources belong to the sequencer and a bar-aligned cut is
  artifact-free by construction. `ForceStopAndSwap()` exists as the explicit instant-cut entry
  point (option (b)).

## 5. Explicitly OUT of scope for this pass

- **Cross-client audio sync.** Each client's music follows its own local threat perception
  (per-client isolation, like CameraModeController/GS-15). Beat-identical playback across the
  4 clients would need RPC-based dspTime negotiation + latency compensation — a separate, harder
  task if ever required.
- **Vertical layering fallback / hybrid.** No stem mixing, no intensity layers within a state.
- **Mixing & ducking vs. SFX/VO** (snapshots, sidechain) — belongs to the AudioMixer setup.
- **Threat heuristics.** What "high threat" means (enemy count, UsedThreat budget, hysteresis)
  lives in the `IThreatStateProvider` implementation, not here.
- **Addressables lifetime management** of clip sets (load/unload on biome change) — caller's
  responsibility; the director assumes assigned sets are loaded.
