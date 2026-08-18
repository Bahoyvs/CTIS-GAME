# CTIS-GAME — *Project C-Building*

A 4-player co-op isometric action-roguelite built in Unity 6 with a fully data-driven ability system and server-authoritative combat.

---

## Tech Stack

| Layer | Technology |
| --- | --- |
| **Language** | C# (~19,600 LOC across 209 scripts) |
| **Engine** | Unity `6000.5.2f1` |
| **Rendering** | Universal Render Pipeline (URP), orthographic isometric "HD-2D" (3D meshes + point-filtered pixel textures) |
| **Networking** | Unity Netcode for GameObjects 2.13, Unity Multiplayer Services (Relay/Lobby) |
| **Camera / Input** | Cinemachine 3.1, Unity Input System 1.19 |
| **AI / Nav** | Unity AI Navigation (NavMesh) 2.0 |
| **Tooling** | ProBuilder, TextMesh Pro, Unity Test Framework |

**Architecture patterns:** ScriptableObject composition, Strategy, Chain of Responsibility, Object Pooling, static event bus, server-authoritative state replication.

---

## Core Mechanics & Features

- **Composes abilities from data, not code.** Every skill is authored as a `ComposedAbilitySO` = *delivery* (how targets are acquired: projectile, arc, line, zone, area, bounce, nearest) × *effects* (what lands: damage, heal, status, displacement) × *team filter* (who is valid). A single `MeleeArc_140deg_3m` delivery asset serves every crescent swing in the game; a dual-payload ability damages enemies and heals allies from the same piercing projectile with zero bespoke code.
- **Drives encounters with an AI Director.** `SpawnDirector` runs server-side and decides *what* (weighted encounter pick under a Threat Capacity budget, re-weighted live by environmental events like Sandstorm or NightPhase), *where* (spawn nodes filtered by a pressure distance band and a reconstructed isometric camera frustum so enemies never pop on-screen), and *when* (randomized pacing plus an Attention meter that releases special-pool elites when player ability usage fills it).
- **Replicates 25 heroes across 4 archetypes.** Guardians, Barriers, Gladiators and Commanders each ship stat data, passive installers and per-skill runtimes; stateful kits (form switches, mounts, tap-vs-hold, tethers) subclass the base runtime while still executing the shared delivery/effect layer instead of re-implementing target acquisition.
- **Runs a four-section roguelite loop.** Sections 1–3 gate progression behind Pixel Point economy → vending-machine boss summon → section clear with full-party revive and biome swap; Section 4 switches genre entirely into a voting/jack-in/escape state machine with a hard timer, floor-by-floor convergence tracking, permadeath and spectator mode.

---

## Technical Architecture & Problem Solving

The hardest problem in this project was **combinatorial explosion in the ability layer, under networking constraints.** With 25 heroes × 4–5 abilities each, the naïve approach — one `MonoBehaviour` per skill — produces well over a hundred near-duplicate classes, each re-implementing overlap queries, root resolution, friend/foe filtering and multi-collider deduplication, and each an independent opportunity to desync. The solution was to invert the axis of reuse: abilities are decomposed into orthogonal `ScriptableObject` strategies (`AbilityDeliverySO` for target acquisition, `AbilityEffectSO` for payload) that designers recombine in the Inspector. Deliveries funnel through a shared `ApplyToOverlaps` path that uses a static `Collider[32]` scratch buffer and a linear-scan dedupe array — no per-cast allocation on the server hot path — while each effect self-filters by team, so one delivery can acquire a mixed crowd and the effects sort it out downstream.

The same "no special cases" discipline governs combat resolution. Rather than branching inside `TakeDamage` for every mark, debuff and anti-heal, each entity carries a `DamageModifierPipeline`: an ordered `IDamageModifier` chain with explicit priority bands (flat → multiplicative → clamps) that every damage and heal amount traverses before armor and health math. Marks, sunburn amplification and anti-heal are all just registered links.

Networking rests on a deliberate authority split, the standard co-op trade-off made explicit: **movement is owner-authoritative** (`ClientNetworkTransform` overrides `OnIsServerAuthoritative()` so heroes respond to input without a server round-trip, which an action game cannot tolerate), while **combat, AI, status effects and spawning stay server-authoritative** — clients receive only a replicated summary struct of control flags and active-effect timers for input gating, VFX and HUD. Status expiry is expressed in `NetworkManager.ServerTime`, not local time, so UI countdowns agree across peers. Finally, mechanics that spawn recursively (fission 1→2→4, clone-on-damage) would stall the server on raw `Instantiate`/`Spawn` churn, so enemies flow through a `NetworkEnemyPool` that registers an `INetworkPrefabInstanceHandler` on every peer — NGO then routes creation and destruction into the pool transparently, and existing `Despawn()` call sites required no changes.

---

## Installation / How to Play

**Requirements:** Unity `6000.5.2f1` (Unity 6). Packages resolve automatically from `Packages/manifest.json`.

```bash
git clone https://github.com/Bahoyvs/CTIS-GAME.git
```

1. Open the cloned folder via **Unity Hub → Add → Add project from disk**.
2. Open `Assets/_Project/Scenes/MainMenu.unity` and press **Play**.
3. Host or join from the lobby (`LobbyScene`), pick a hero, and start the run in `GameScene`.

**Multiplayer testing:** use **Multiplayer Play Mode** (Window → Multiplayer → Multiplayer Play Mode) to run additional virtual players in-editor, or build a standalone player and connect it to the editor host.

| Action | Input |
| --- | --- |
| Move | `WASD` / arrow keys |
| Aim / face | Mouse position (cursor raycast) |
| Basic attack | `LMB` |
| Skill 1 / Skill 2 | `Q` / `E` |
| Ultimate | `X` |
| Roll | `Left Shift` |
| Interact | `F` |

**Design documentation:** the full GDD lives in [`Documentation/GDD/`](Documentation/GDD/) — core vision, gameplay loop, hero system architecture, item system, five biomes, boss encounters and the complete 25-hero roster.
