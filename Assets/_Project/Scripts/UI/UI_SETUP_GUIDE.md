# CTIS In-Game HUD — Setup Guide (GS-16)

Event-driven HUD bound to the real `CBuilding` framework. No script polls UI state in `Update()`: widgets subscribe to `NetworkVariable.OnValueChanged`, `NetworkList.OnListChanged`, and the framework's owner-mirror events; animation coroutines are only *started* by those events.

## Script Map

```
Assets/_Project/Scripts/UI/
├── Core/   UIPalette, EffectIconCatalog (SO)
├── HUD/    PlayerHUDController, SegmentedHealthBar, ShieldBar,
│           StatusEffectIconRow, StatusEffectIconWidget,
│           AbilityBarController, AbilitySlotWidget
├── Team/   TeammatePanelController, TeammateWidget
└── World/  UIBillboard, EnemyWorldUI
```

All in namespace `CBuilding.UI`.

## What the HUD binds to (framework integration)

| HUD element | Data source |
|---|---|
| Player health (chunked) | `BaseHero.OnHealthChanged (current, max)` |
| Player shield | none yet — widget wired, driven to 0 until a shield system lands |
| Status icon row | `StatusEffectController.SyncedEffects` (NetworkList) + `EffectIconCatalog` hash lookup |
| Ability icons | `AbilityController.GetAssignedData(slot).icon` (any-peer) |
| Cooldown wipe | `AbilityController.OnCooldownUpdated (slot, remaining, duration)` — owner mirror; refunds/ReduceAllActive just re-fire it |
| Charge pips (Kerem) | `AbilityController.OnChargesUpdated (slot, charges)` — ChargeBased slots only |
| Teammate health arc | ally `BaseHero.OnHealthChanged` |
| Teammate death blackout | ally `BaseHero.OnDied` (ClientRpc — fires on all peers); heal > 0 clears it |
| Teammate ult LED | `AbilityController.NetUltimateReady` (NetworkVariable, everyone-read) |
| Voice LED | `TeammateWidget.SetSpeaking(bool)` — wire to Vivox later |
| Enemy billboard health | `BaseEnemy.NetHealth` / `BaseEnemy.MaxHealth` |
| Enemy debuff slot + ice frame | enemy `StatusEffectController.SyncedEffects` + `ControlFlags.Freeze` |

Framework additions made for GS-16 (all additive):
`HeroRole` enum + `HeroStatsData.Role` field · `AbilityController.NetUltimateReady`, `OnChargesUpdated`, `GetAssignedData()` · `BaseEnemy.NetHealth`/`MaxHealth` accessors · `BaseHero.ActiveHeroes` registry + `OnHeroSpawned`/`OnHeroDespawned` static events.

> **Editor step:** set the `Role` field on each hero's `HeroStatsData` asset (Ironworks/Ug = Tank, Kerem/AP = DPS, Bahadır/Ok = Controller, TL/Gobluna = Support).

## 1. Main HUD Canvas (Screen Space – Camera)

1. `GameObject > UI > Canvas` → `HUD_Canvas`.
   - Render Mode: **Screen Space – Camera**, Render Camera: main iso camera, Plane Distance ≈ 1.
   - Canvas Scaler: Scale With Screen Size, 1920×1080, Match 0.5.
2. Add `PlayerHUDController` to `HUD_Canvas`.

### A. Vitals (Top Left)

```
Vitals (anchor top-left)
├── HealthBar → SegmentedHealthBar
│   ├── BG (dark, UIPalette.DepletedSlot, ~360×26)
│   └── Segments (HorizontalLayoutGroup, spacing 2)
│       └── Seg_0 … Seg_9   Image, white square sprite, Filled/Horizontal/Origin=Left
├── ShieldBar → ShieldBar (height ~8)
│   └── Fill               Image, Filled/Horizontal/Origin=Left
└── StatusRow → StatusEffectIconRow (HorizontalLayoutGroup, spacing 4)
    └── EffectIcon_0 … _5  → StatusEffectIconWidget (inactive by default)
        ├── BG   black square ~28×28
        ├── Icon (sprite set at runtime from EffectDataSO.icon)
        └── Ring thin ring, Filled, Radial 360, Origin=Top, **Clockwise=OFF**
```

Create the catalog: `Assets > Create > CBuilding > UI > Effect Icon Catalog`, drag in every `EffectDataSO`, assign to `StatusEffectIconRow` (and to each enemy's `EnemyWorldUI`).

### B. Ability Bar (Bottom Center)

```
AbilityBar → AbilityBarController (anchor bottom-center)
└── Slot_RMB / Slot_Q / Slot_E / Slot_F → AbilitySlotWidget (~72×72, spacing 12)
    ├── Ring          circle outline (class-colored at bind)
    ├── Disc          solid black circle
    ├── Icon          auto-pulled from the slot's AbilityDataSO.icon
    ├── CooldownMask  circle, Filled, Radial 360, Origin=Top, **Clockwise=ON**
    ├── PulseRing     duplicate of Ring, inactive
    ├── Pips          tiny squares, bottom-right exterior
    └── Counter       TMP text, tiny cyber font, bottom-right exterior
```

Slot mapping is fixed in code: RMB=Feature, Q=Skill1, E=Skill2, F=Ultimate (Passive/FinalPassive have no circle).

### C. Teammate Panel (Bottom Left)

```
TeamPanel → TeammatePanelController (VerticalLayoutGroup)
└── Teammate_0 / _1 / _2 → TeammateWidget (~64×64, inactive until bound)
    ├── Frame       circle outline (class color from HeroStatsData.Role)
    ├── Avatar      pixelated head/helmet silhouette
    ├── HealthArc   ring, Filled, Radial 360, Origin=Bottom, **Clockwise=OFF** (left half)
    ├── ShieldArc   ring, Filled, Radial 360, Origin=Bottom, **Clockwise=ON**  (right half)
    ├── DeadOverlay blackout circle + red diagonal cross (inactive)
    ├── UltLed      ~8px circle, right of avatar, top
    └── VoiceLed    ~8px circle, right of avatar, bottom
```

## 2. Enemy Billboard (World Space micro-canvas)

On each enemy prefab (root already has `NetworkObject` + `BaseEnemy` + `StatusEffectController`):

```
Enemy
└── WorldUI (Canvas: World Space, above head, scale ~0.01, NO GraphicRaycaster)
    │  + UIBillboard, + EnemyWorldUI
    ├── HealthBG     dark strip
    ├── HealthFill   red strip, Filled/Horizontal/Origin=Left (smooth deplete)
    ├── ShieldLayer  (auto-hidden — enemy shields don't exist yet)
    ├── DebuffSlot   small square glued to the far-LEFT edge (inactive)
    └── FrozenFrame  ice-crystal frame sprite (inactive; driven by ControlFlags.Freeze)
```

## 3. Art / Import Rules

Plain white flat sprites, tinted from `UIPalette` in code. Sprite / Compression None / Point filter for pixelated avatars. No shadows, glows, gradients, or outlines — the HUD's saturated flats are the only color accents against the desaturated world.

## 4. Testing (host mode)

Damage/heal a hero or enemy through the existing damage pipeline (any delivery/effect asset). Status icons appear the moment `ApplyEffect` runs; cooldown wipes fire on any `TryActivate`. For multi-client checks use Multiplayer Play Mode.
