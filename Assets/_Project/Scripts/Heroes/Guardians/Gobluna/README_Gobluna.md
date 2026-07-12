# Gobluna — Guardian / Ranged / Fate (Siphoner)

Kit implementation on top of GS-9 (Composed Ability System) and GS-17 (Unified Basic
Attack). Asset folder convention: `_Project/Data/Heroes/Guardians/Gobluna/{Abilities,CA,Delivers,Effects,Prefabs}`
(mirrors Bahadır's layout).

---

## 1. Prefab: `Hero_Gobluna`

Components (all siblings, same as every hero prefab):

| Component | Notes |
|---|---|
| `HeroController` + `HeroStatController` + `StatusEffectController` + `DamageModifierPipeline` | stock |
| `AbilityController` | slot assignments below |
| `ComposedBasicAttackBehaviour` | **unassigned** — the "Bounce Orb" asset that used to sit here was a shared basic-attack archetype mistakenly wired to Gobluna only; it has been moved to `_Project/Data/BasicAttacks/BounceOrb` (`CA_BounceOrb_Sec1/2/3`) so any hero can pick it up. Gobluna needs her own real basic attack assigned. |
| `GoblunaHeroController` | **bespoke** — Siphoner, Green Fire mark, Ult loop, CD override, leap RPC |
| `GoblunaSkill2Controller` | **bespoke** — lock / resource bar / purge-stun |

`AbilityController` slots: Feature = `Ability_Gobluna_Feature`, Skill1 = `Ability_Gobluna_Skill1`,
Skill2 = `Ability_Gobluna_Skill2`, Ultimate = `Ability_Gobluna_Ult`.
Passives have **no slot asset** — they live entirely in `GoblunaHeroController` (event-driven,
no `IPassiveTrigger` tick needed).

---

## 2. Status effect assets (`CBuilding/Status Effects/Effect`)

| Asset | Key fields | Used by |
|---|---|---|
| `Fx_GreenFireMark` | duration **9999** (permanent), tickInterval 0, damagePerTick 0, StackingPolicy **Ignore**, isDebuff ON | Passive — stamped on everything she damages. Pure mark (UI icon / future synergies). **Must be a different asset from the DoT below**, or the passive would lock Skill2 forever. |
| `Fx_GreenFire` | duration **9999**, tickInterval 1, damagePerTick ~4, StackingPolicy **Ignore**, isDebuff ON | Skill2 cone DoT. The lock and the purge-stun key off THIS asset (assign it to `GoblunaSkill2Controller.greenFireDoT` **and** inside `CA_Gobluna_S2_Cone` — same reference). |
| `Fx_GoblunaStun` | duration 1.5–2, ControlFlags **Stun**, StackingPolicy Refresh, isDebuff ON | Skill2 purge (`purgeStunEffect`). `BaseEnemy` now honors Stun flags (this change also makes Bahadır's stuns real). |
| `Effect_GoblunaUltMode` | duration **18**, no flags, no ticks, StackingPolicy Refresh, isDebuff **OFF** | Applied to self by the Ult cast; `GoblunaHeroController` reacts to apply/expire (CD override + blast loop). Duration IS the mode length — tune here, nowhere else. |
| `Fx_GoblunaLeapRoot` | duration 0.35 (≈ leapDuration), ControlFlags **Root**, isDebuff OFF | Feature — stops WASD fighting the owner-side leap. |

DoT ticks carry `Instigator = source` → every Green Fire tick feeds Siphoner + the resource
bar automatically. That's the intended engine of the kit; `siphonRatio` and `damagePerTick`
are the tuning valves if it sustains too hard.

## 3. Delivery assets

| Asset | Type | Key fields |
|---|---|---|
| `Del_Gobluna_S1_Darts` | `ProjectileDeliverySO` | **count 3, spreadAngle ~25, pierceCount 99**, speed 16, maxRange 9, explosionRadius 0, retarget OFF, hitLayers = Enemy \| Hero (allies must be hittable to be healable!). Prefab: shared `AbilityProjectile` prefab. |
| `Del_Gobluna_S2_Cone` | `ArcDeliverySO` | range 6–7, **arcAngle 180** ("massive frontal half-circle"), hitLayers = Enemy. |
| `Del_Gobluna_Feature_Zone` | `ZoneDeliverySO` | castRange 0 (dropped programmatically at the ally), radius 3, **duration 6, tickInterval 1**, hitLayers = Hero. Prefab: shared `AbilityZone` prefab (green ring visual). `AbilityZone` team-filters already — no ally-flavored `EnemyHazardZone` needed. |
| `Del_Self` | `SelfDeliverySO` | shared existing asset (Ult cast). |

## 4. Effect assets (`CBuilding/Abilities/Effects/...`)

| Asset | Type | Key fields |
|---|---|---|
| `Fx_Gobluna_DartDamage` | Damage | ~14, appliesTo **Enemies** |
| `Fx_Gobluna_DartHeal` | Heal | ~12, appliesTo **Allies** (darts spawn in front of her — she can't dart-heal herself anyway) |
| `Fx_Gobluna_ApplyGreenFire` | Apply Status | statusEffect = `Fx_GreenFire`, appliesTo **Enemies** |
| `Fx_Gobluna_ZoneHeal` | Heal | ~8 per tick, appliesTo **AlliesAndSelf** |
| `Fx_Gobluna_FullSelfHeal` | Heal | **99999** (clamped by ServerHeal), appliesTo AlliesAndSelf |
| `Fx_Gobluna_ApplyUltMode` | Apply Status | statusEffect = `Effect_GoblunaUltMode`, appliesTo AlliesAndSelf |

## 5. Composed ability assets (`ComposedAbilitySO`)

| Asset | Delivery | TeamFilter | Effects | AbilityDataSO fields |
|---|---|---|---|---|
| `Ability_Gobluna_Skill1` | `Del_Gobluna_S1_Darts` | **EnemiesAndAllies** | DartDamage + DartHeal | mode Instant, **cooldown 1.0** |
| `CA_Gobluna_S2_Cone` | `Del_Gobluna_S2_Cone` | Enemies | ApplyGreenFire (+ optional small Damage) | referenced by `Ability_Gobluna_Skill2`, never slotted directly |
| `Ability_Gobluna_Ult` | `Del_Self` | AlliesAndSelf | FullSelfHeal + ApplyUltMode | mode Instant, cooldown 60+ |

Bespoke `AbilityDataSO` assets: `Ability_Gobluna_Skill2` (`GoblunaSkill2DataSO`, coneAbility =
`CA_Gobluna_S2_Cone`; OnValidate forces mode Instant / cooldown 0) and `Ability_Gobluna_Feature`
(`GoblunaFeatureDataSO`: leapRange 10, leapDuration 0.35, selfRootEffect = `Fx_GoblunaLeapRoot`,
healZoneAbility = `CA_Gobluna_Feature_Zone`, cooldown ~12).

## 6. New prefab: `Proj_Gobluna_AllyBlast`

`AllyBounceProjectile` + `NetworkObject` + `NetworkTransform` (server-auth) + child visual.
Register in the Network Prefabs list. Assign to `GoblunaHeroController.allyBounceProjectilePrefab`.
Serialized tuning lives on the prefab: speed 5 (slow!), healPerArrival 30, damagePerPass 15,
allySearchRadius 14.

## 7. Flow summary (who talks to whom)

```text
any Gobluna damage → BaseEnemy.TakeDamage → TeamEventBus.OnAllyDealtDamage
    → GoblunaHeroController: Green Fire mark + Siphoner heals (registry scan near victim)
        → BaseHero.ServerHeal (returns REAL healing)
            → TeamEventBus.OnAllyHealedAlly ← also raised by HealEffectSO + AllyBounceProjectile
                → GoblunaSkill2Controller: resource += healed × rate

S2 cast (unlocked) → cone → ApplyStatusEffectSO.OnAnyStatusApplied(Fx_GreenFire, HER)
    → burning set grows → NetLocked = true
S2 cast (locked + bar full) → purge: bar = 0, stun + strip fire, unlock
burning enemy dies / pool-clears → set shrinks → empty = unlock (bar KEPT)

Ult cast → CA applies full heal + Effect_GoblunaUltMode to self
    → OnEffectApplied → SetCooldownOverride(Skill1, 0.4) + blast loop (t=0, then every 5s)
    → OnEffectExpired (18s) → ClearCooldownOverride + stop loop
```

## 8. Shared-core changes made for this kit (all hero-agnostic)

1. `TeamEventBus` — `OnAllyDealtDamage`, `OnAllyHealedAlly` (precedent: `OnAllyKilledEnemy`).
2. `BaseEnemy` — raises `OnAllyDealtDamage` on hero-sourced hits; **now honors ControlFlags.Stun/Freeze** (closes the README TODO; Bahadır benefits too).
3. `BaseHero.ServerHeal` — returns the HP actually restored (anti-heal + overheal aware).
4. `HealEffectSO` — announces real healing on the bus.
5. `CooldownManager` — `SetCooldownOverride` / `ClearCooldownOverride` (precedent: `ReduceAllActive` shared API).
6. `AbilityProjectile` — only HOSTILE hits consume pierce (ally heals in passing don't shorten a dart). Enemies-only abilities unaffected.

## 9. Open item

Feature input: like Bahadır (Roll-triggered Feature via `BahadirRollController`), Gobluna's
Feature needs an input trigger decision — either a `GoblunaRollController : IRollBehaviour`
that fires `TryActivate(AbilitySlot.Feature)` on roll, or a dedicated Feature action in the
Input Actions asset. Everything server-side is ready; only the owner-side trigger is a
design call.
