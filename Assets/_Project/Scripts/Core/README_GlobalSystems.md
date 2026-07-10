# Global Systems Foundation (GS plan)

Implements the foundation tier of `C-Building_Global_Sistemler_Mimari_Plani.md`,
integrated with the existing `CBuilding.*` codebase (no duplicate contracts).

| Location | System | Plan ref |
|---|---|---|
| `Core/Network/NetworkGameManager.cs` | Session state, 4-player cap, player registry | GS-1 |
| `Core/Combat/` | `IDamageModifier` chain + `DamageModifierPipeline` | GS-5.4 |
| `Core/IDamageable.cs` | Existing contract, extended with `DamageFlags` | GS-5.4 |
| `Core/StatusEffects/` | `IStatusEffect`, `StatusEffectController`, `EffectDataSO` catalog | GS-5 |
| `Core/Abilities/` | `AbilityDataSO`/`AbilityRuntime`, slot contract, `CooldownManager` | GS-9 |

## Integration decisions

- **Existing contracts win.** `CBuilding.Core.DamageInfo`/`IDamageable` and the
  health NetworkVariables in `BaseHero`/`BaseEnemy` are kept; the GS code plugs
  into them instead of duplicating them. `DamageInfo` gained an optional
  `DamageFlags` field (old call sites unaffected).
- **GS-5.4 pipeline** is wired into `BaseHero.TakeDamage`/`ServerHeal` and
  `BaseEnemy.TakeDamage`. Marks (SpywareMark, Mark of Guilt), Sunburn and
  anti-heal are `IDamageModifier`s — never special-cased at call sites.
- **`AbilityController`** is a sibling component of the concrete hero class:
  assign the six slot assets (Feature/Passive/FinalPassive/Skill1/Skill2/Ultimate)
  in the inspector; `HeroController` input calls `TryActivate(slot)`.
  `BaseHero.PerformSkill1/2/Ultimate` virtuals can delegate here as kits migrate.
- **`StatusEffectController`** is a sibling of `BaseHero`/`BaseEnemy`; it auto-adds
  `DamageModifierPipeline`. Movement code should multiply by its
  `MoveSpeedMultiplier` and check `CanMove` (TODO when kits migrate).
- **No asmdef** — everything stays in Assembly-CSharp like the rest of `_Project`.

## Core rules (from the plan)

- Single sync pattern: `NetworkVariable<T>`, server-write / everyone-read (GS-1.3).
- No hero-specific branching in `AbilityController`/`CooldownManager` (GS-9.4) —
  variation lives in `AbilityDataSO.mode` and `AbilityRuntime` subclasses.
- `CooldownManager.ReduceAllActive(seconds)` is shared by Bahadır's Skill2 (GS-9),
  boss member death (GS-13) and Network Override (GS-14). Do not fork it.
- Permadeath (GS-4): only the sanctioned 'Debugger' item may bypass it (GS-11).

## Not yet implemented (next tiers)

GS-2 SectionManager, GS-4 PlayerLifeState, GS-6 biome bars, GS-7 hazards,
GS-8 enemy modules, GS-10 labels/synergy.
