# C-Building — Main Menu & Lobby Setup Guide

Target: Unity 6000.5, Netcode for GameObjects 2.13, UnityTransport.

Scripts involved (all under `Assets/_Project/Scripts/`):

| Script | Location | Responsibility |
|---|---|---|
| `MainMenuManager.cs` | `UI/MainMenu/` | Host/Join, name entry, connection approval (max 4) |
| `LobbyNetworkManager.cs` | `Lobby/` | `NetworkList<LobbyPlayerState>` sync, hero locking, ready, start |
| `LobbyUIManager.cs` | `Lobby/` | Top bar, role tabs, roster grid, Ready/Start button |
| `LobbyPlayerSlotUI.cs` / `HeroRosterButtonUI.cs` | `Lobby/` | Dumb view widgets |
| `LobbyAvatarSpawner.cs` | `Lobby/` | Local-only 2D figures at the 4 desks |
| `HeroCatalogSO.cs` / `HeroStatsData.cs` | `Data/` | Hero database (id = catalog index) |

---

## 1. Scenes & Build Profile

1. Create three scenes: `MainMenu`, `LobbyScene`, `GameScene` (e.g. in `Assets/_Project/Scenes/`).
2. **File ▸ Build Profiles ▸ Scene List** — add all three, `MainMenu` at index 0.
   NGO's networked scene loading refuses scenes that aren't in the build list.

## 2. Persistent NetworkManager (MainMenu scene)

1. In `MainMenu`, create GameObject `NetworkManager`.
2. Add **NetworkManager** component → in *Select Transport*, pick **UnityTransport**.
3. In the NetworkManager inspector:
   - **Player Prefab: leave EMPTY** (approval callback sets `CreatePlayerObject = false`; the lobby never spawns players — `PlayerSpawner` does that in GameScene).
   - **Enable Scene Management: ON** (required for `SceneManager.LoadScene` sync).
   - **Connection Approval: ON** (MainMenuManager uses it for the 4-player cap and to block mid-match joins).
4. NGO keeps this object alive across scene loads automatically — don't add your own DontDestroyOnLoad.
5. Note: the old `NetworkSessionManager`/`LobbyMenuUI` (single-scene MVP flow) should NOT be active in this scene — `MainMenuManager` replaces them for the menu→lobby flow. Remove them from the NetworkManager object or leave them disabled.

## 3. MainMenu Canvas

```
Canvas (Screen Space - Overlay, Canvas Scaler: Scale With Screen Size 1920×1080)
└─ MainMenuPanel
   ├─ Title (TMP_Text)                     "C-BUILDING"
   ├─ PlayerNameInput (TMP_InputField)     placeholder "Your name"
   ├─ AddressInput (TMP_InputField)        placeholder "127.0.0.1"
   ├─ PortInput (TMP_InputField)           placeholder "7777"
   ├─ HostButton (Button + TMP label)      "HOST GAME"
   ├─ JoinButton (Button + TMP label)      "JOIN GAME"
   └─ StatusText (TMP_Text)
```

Add `MainMenuManager` to any scene object (e.g. the Canvas), wire all references, and set
*Lobby Scene Name* = `LobbyScene`, *Game Scene Name* = `GameScene` (must match scene file names exactly).

## 4. Hero Data

### 4a. HeroStatsData additions
Each existing hero asset (Kerem, Bahadır, …) now has a **Presentation (Lobby & UI)** section:
- **Icon** — portrait sprite (roster grid + top bar).
- **Lobby Avatar Prefab** — see §7. If left empty, the lobby stands the Icon sprite at the desk (fine for graybox).
- **Gameplay Prefab** — the real networked hero, for GameScene spawning (§9).

### 4b. HeroCatalog asset
1. **Assets ▸ Create ▸ C-Building ▸ Data ▸ Hero Catalog** → save as `Assets/_Project/Data/HeroCatalog.asset`.
2. Drag every `HeroStatsData` asset into its `Heroes` list.
3. ⚠ **Index = network hero id. Append only — never reorder a shipped list**, or clients on different builds will disagree about who picked whom.

Tab ↔ role mapping (no enum rename needed, handled by `HeroCatalogSO.GetRoleDisplayName`):
`Assault → DPS`, `Support → Support`, `Control → Controller`, `Defense → Tank`.

## 5. LobbyScene — network + room

1. **LobbyNetworkManager**: empty GameObject → add `NetworkObject` + `LobbyNetworkManager`.
   Assign *Hero Catalog*; scene names default to `GameScene` / `MainMenu`.
   (In-scene-placed NetworkObject: NGO synchronizes it automatically, no registration needed.)
2. **The room**: sprites for the background and **4 desks**. Create empty GameObject `DeskAnchors`
   with 4 children `Desk_0 … Desk_3` (left→right), each positioned where a hero should stand.
   Desk order == top-bar slot order == join order.
3. **LobbyAvatarSpawner**: empty GameObject → add `LobbyAvatarSpawner`; assign *Hero Catalog* and drag `Desk_0…3` into *Desk Points*.
4. A normal Camera framing the room (lobby is a real 2D scene, not just UI).

## 6. LobbyScene — Canvas

```
Canvas
├─ TopBar (HorizontalLayoutGroup)
│  └─ PlayerSlot_0 … PlayerSlot_3        (prefab, LobbyPlayerSlotUI)
│     ├─ NameText (TMP)  ├─ StatusText (TMP)
│     ├─ HeroIcon (Image) ├─ ReadyBadge (GameObject: "READY" tag/checkmark, default OFF)
│     └─ CanvasGroup (on the root, for dimming empty slots)
├─ BottomBar
│  ├─ TabRow (HorizontalLayoutGroup)
│  │  └─ Tab_0 … Tab_3 (Button + TMP label — labels are set from code in tab order:
│  │                     Assault, Support, Control, Defense)
│  └─ RosterScroll (ScrollRect) ▸ Viewport ▸ RosterGrid (GridLayoutGroup, e.g. cell 96×120)
├─ ReadyStartButton (Button) ▸ ReadyStartLabel (TMP)
├─ LeaveButton (Button)
└─ HintText (TMP)
```

**HeroRosterButton prefab** (`HeroRosterButtonUI`):
Button root + `Icon` (Image), `NameText` (TMP), `SelectedFrame` (highlight border, default OFF),
`LockedOverlay` (semi-transparent dimmer/padlock, default OFF). Save as prefab, do NOT place in scene.

Add `LobbyUIManager` to the Canvas and wire: catalog, the 4 slot widgets, the roster button prefab,
`RosterGrid` as grid parent, the 4 tab buttons **in tab order**, Ready/Start button + label, Leave button, hint text.
The grid populates itself from the catalog at runtime — never hand-place hero buttons.

## 7. Lobby avatar prefabs (optional but recommended)

Per hero: GameObject with `SpriteRenderer` (idle sprite) and optionally an `Animator` (idle loop).
**No NetworkObject, no controllers, no rigidbodies** — these are local-only props; every client
derives them from the replicated list, so networking them would be redundant traffic.
Assign to the hero's *Lobby Avatar Prefab* field.

## 8. How the sync works (reference)

- Server owns `NetworkList<LobbyPlayerState>`; rows added/removed on connect/disconnect.
- Clients send `[Rpc(SendTo.Server)]` requests: name (auto on spawn, from PlayerPrefs), hero select, ready toggle.
- **Unique selection** is enforced server-side in `SelectHeroServerRpc` (`IsHeroTakenByOther`) —
  the UI graying-out is cosmetic; a race between two clicks is resolved by whoever the server processes first.
- Ready requires a hero; picking again while ready is rejected; UI locks the grid when ready.
- Host's button becomes **START GAME** when every connected player (host included) is ready →
  `NetworkManager.SceneManager.LoadScene("GameScene")` moves everyone.
- Late joiner: approval callback rejects if 4/4 or already in GameScene; otherwise NGO scene-sync
  drops them into LobbyScene and the full list state arrives on spawn.

## 9. GameScene integration (next step)

Before loading GameScene, the lobby snapshots picks into `LobbyNetworkManager.HeroSelections`
(static, server-only, `clientId → heroId`). Update `PlayerSpawner.SpawnHeroFor` to:

```csharp
int heroId = LobbyNetworkManager.HeroSelections.TryGetValue(clientId, out int id)
    ? id : 0; // fallback for direct-scene testing
var prefab = heroCatalog.GetHero(heroId).GameplayPrefab.GetComponent<NetworkObject>();
```

Each `GameplayPrefab` must be registered in the NetworkManager's Network Prefabs list.

## 10. Test checklist

1. Multiplayer Play Mode (or a second build): instance A hosts → lands in LobbyScene, slot 0 filled.
2. Instance B joins `127.0.0.1` → slot 1 fills on both screens; names match inputs.
3. A picks a hero → figure appears at Desk 0 on BOTH instances; that hero is locked/dimmed for B.
4. B tries A's hero → click rejected (button non-interactable; server would reject anyway).
5. Both ready → host button flips to START GAME → GameScene loads for both.
6. B disconnects in lobby → B's slot empties, desk clears, B's hero unlocks for A.
7. Fifth client join attempt → rejected with "Lobby is full (4/4)".
