# _Project — Folder Architecture

Everything custom-built lives under `_Project` so it stays separate from Unity's default
`Scenes/`, `Settings/`, and any imported third-party assets. Backend analogy: this is your
`src/` — the rest of `Assets/` is closer to `node_modules/`.

## Scripts/ (your application code)

- **Core/** — game/session-level singletons: `GameManager`, `SectionManager`, `VendingMachine`,
  `PortalChallengeManager`. Analogous to your app's top-level services/orchestrators.
- **Heroes/Base/** — the abstract `BaseHero` class (movement, health, the shared kit contract:
  BasicAttack, Roll, Feature, Passive, FinalPassive, Skill1, Skill2, Ultimate).
- **Heroes/{Guardians,Barriers,Gladiators,Commanders}/** — concrete `HeroController` subclasses
  per role, one script per hero (e.g. `KeremController : BaseHero`). Inheritance mirrors an
  abstract base class + subclasses in any OOP backend.
- **Enemies/Base/** + **Enemies/{Forest,Void,Frozen,Desert,Family}/** — same pattern for enemy
  AI, organized by the biome they belong to (see GDD section 5).
- **Bosses/** — boss-specific controllers (e.g. the 5-unit "Family" boss coordinator).
- **Items/** — Teachers' Office item behaviors (GDD section 4).
- **Data/** — the ScriptableObject *class definitions* only: `HeroData.cs`, `ItemData.cs`,
  `EnemyData.cs`, `BossData.cs`. Think of these as your schema/model definitions.
- **UI/**, **Utilities/** — HUD/menus and shared helpers.

## Data/ (the "rows", not the "schema")

Actual ScriptableObject *instances* (`.asset` files) created from the classes in
`Scripts/Data/`. Mirrors the difference between a Mongoose schema (code) and the documents in
a collection (data): `HeroData.cs` is the schema, `Data/Heroes/Guardians/Echo.asset` is a row.

## Prefabs/, Art/, Audio/

Standard content buckets, split by category to avoid one giant flat folder as the project
scales.

## Where the design content lives

All GDD content (hero kits, enemy stats, biome hazards, boss mechanics) is in
`Documentation/GDD/` at the project root — not duplicated here. Scripts and data assets should
reference hero/enemy names consistently with those docs.
