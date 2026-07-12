# GS-17 Implementation Notes (v1.2 + recommendation set)

Code-complete implementation of GS-17 (Unified Basic Attack & Visual Weapon System),
including the 12 recommendations pending Kerem's sign-off. Where a recommendation was a
*suggestion*, it's implemented behind a flag/parameter so a different call later is a
data change, not a rewrite.

## What was built

**Modified**
- `Core/Abilities/AbilityEnums.cs` — `AbilitySlot.BasicAttack` added.
- `Heroes/Base/HeroController.cs` — hardcoded OverlapSphere melee **deleted**. Basic attack
  routes through `IBasicAttackBehaviour` (discovered via `GetComponent` in Awake). Server
  cooldown now lives in the shared `CooldownManager` under the `BasicAttack` slot, duration
  re-read from `Stats.GetStat(StatType.AttackCooldown)` every trigger. Tap/hold input branch
  (`Attack.started`/`.canceled`, 0.18s threshold). New 1-byte quantized aim NetworkVariable
  (3° threshold + 14 Hz cap); remotes reconstruct `AimDirection` in the value-changed
  callback. `AttackSwingClientRpc` now triggers weapon recoil on every client.
- `Core/Abilities/Delivery/Deliveries/ProjectileDeliverySO.cs` — the doc's `maxPierceHits`
  already existed as `pierceCount` (Kerem's "pierce 1, dissipate on 2nd" = **2**). Added
  `canPierceWalls` + `wallLayers`, and cone-limited `retargetOnApproach` homing (rec #12).
- `Core/Abilities/Delivery/AbilityProjectile.cs` — wall blocking + one-time approach retarget.

**New**
- `Core/GameFlow/SectionManager.cs` — networked Section 1/2/3 authority, static `OnSectionChanged`.
- `Heroes/Base/IBasicAttackBehaviour.cs` — `IBasicAttackBehaviour` + `IHoldableBasicAttack`
  (with `HoldEnabled(hero)` so AP's hold only exists in Ultimate Mode).
- `Heroes/Base/ComposedBasicAttackBehaviour.cs` — 3 per-Section SOs, null falls back to previous tier.
- `Heroes/Base/WeaponVisualController.cs` — rotate (MoveTowardsAngle) / flipY hysteresis /
  live iso sorting (`invertNorthSouth` flag = Open Question #3's one-line fix) / recoil
  coroutine **clamped below the current attack cooldown** (rec #2).
- `Core/StatusEffects/StackingMarkEffectSO.cs` — N-stack mark, per-stack damage amp,
  `sourceLocked` flag (rec #4: default ON), `OnMaxStacksReached` event, `IsFullyStacked`/`IsFrom` queries.
- `Core/Abilities/BasicAttackComboTracker.cs` — cast-streak variant (pure modulo, decay off
  by default) + per-target variant (4.5s decay), rec #11.
- `Core/Abilities/Delivery/Deliveries/BounceDeliverySO.cs` + `Delivery/BounceProjectile.cs` —
  rec #10 in full: enemy-priority, closest-to-prev + lowest-HP% tie-break, excludes only the
  immediately prior target, `allowAllyBounce`/`allowSelfBounce` per Section asset.
- `Heroes/Gladiators/Kerem/KeremBasicAttackController.cs` — spread ability tap, telekinesis
  hold (grab-time lock per rec #6, §4-style drag sync, slam resolves dead=skip/shield=normal).
- `Heroes/Gladiators/AP/APBasicAttackController.cs` — Royalty Points (uncapped attack speed
  as one recomputed `StatModifier`), Section-gated chain shots via one `FireShot(...,
  countsForResource)` path, raw-damage-only chain asset (rec #5), ult-mode hold-to-beam.

## Editor setup (per hero)

1. **Scene**: add a `SectionManager` (+ NetworkObject) next to NetworkGameManager.
2. **Prefab hierarchy**: `WeaponPivotSocket` (hand/hip anchor) → `WeaponVisual`
   (SpriteRenderer + `WeaponVisualController`). Wire Owner + Hero Body Sprite. Sprites
   authored pointing +X/east; use Sprite Forward Offset otherwise. Recoil per weight class:
   Barriers larger/slower, Gladiators smaller/faster (authored, not formula — rec #2).
3. **Generic heroes (6)**: add `ComposedBasicAttackBehaviour`, create up to 3 basic-attack
   `ComposedAbilitySO` assets (S1 required; S2/S3 only when the archetype tier changes
   behavior). Melee = Zone/Line delivery, Ranged = Projectile. TL's piercing vine = plain
   **Line** delivery, pierce naturally uncapped (rec #1 — no new Delivery type).
4. **Kerem**: add `KeremBasicAttackController`. Assets: spread ability (Projectile: count 3,
   spreadAngle ≈16°, **pierceCount 2**; effects Damage + ApplyStatusEffect(StackingMark)),
   `Fx_StackingMark_Kerem` (StackIntensity, maxStacks 4, sourceLocked ON), optional grabbed
   stun EffectDataSO.
5. **AP**: add `APBasicAttackController`. Assets: primary shot (full effects, longer
   maxRange), chain shot (**DamageEffect only** — rec #5), beam tick ability (Line delivery)
   for Ultimate Mode. Whatever runtime implements AP's Ultimate must set
   `HeroController.IsInUltimateMode`.
6. **Archetype companions**: add `BasicAttackComboTracker` to Cleave Sweep / Recycler Claws /
   Rapid Needle carriers; subscribe the Section-tier bonus to its events.
7. Register new projectile prefabs (`BounceProjectile`) in the Network Prefabs list.

## Test checklist (ParrelSync, 2+ clones)

- Owner: weapon tracks mouse instantly; remote: smooth (not stepped) despite 1-byte sync —
  raise `rotationSpeedDeg` if choppy.
- **North sign check (Open Q #3)**: aim due north on MainIsoCam, watch sorting. Wrong way
  around → tick `invertNorthSouth` on WeaponVisualController; fixes all 8 heroes at once.
- Attack-speed buff mid-fight → next swing already uses the new cooldown (no recalc system).
- Kerem: tap < 0.18s = spread; hold = grab only fully-marked (4-stack) enemies near cursor;
  target dying mid-drag skips the slam; releasing slams the rest through shields.
- AP: Royalty only on primary shots; S2/S3 chain counts flip live when SectionManager advances.

## Deferred / flagged

- **Stats target-context API** (`GetStat(stat, ITargetContext)`) for AP's marked-target
  attack speed — deferred until that mechanic's numbers exist; current API untouched.
- **Grab VFX**: `KeremBasicAttackController.GrabbedIds` (synced NetworkObjectId list) is the
  GS-16 integration point — visuals not built here.
- Doc v1.3 update: pending Kerem's confirmations on the 12 recommendations
  (Trinity Brawler S2 reading A vs B, Impact Slam execute threshold, Cleave Sweep base
  counter text, Flak Shot stun balance).
