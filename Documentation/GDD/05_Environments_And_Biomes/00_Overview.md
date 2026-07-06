# 5. Environments & Biomes (C-Building Mutations)

The C-Building has fractured into distinct dimensional biomes, each featuring unique hazards and specialized enemies.

- [Forest Section](Forest_Section.md)
- [Void Section](Void_Section.md)
- [Frozen Section](Frozen_Section.md)
- [Desert Section](Desert_Section.md)
- [Family Section](Family_Section.md) (leads into [The Family boss](../06_Boss_Encounters/The_Family.md))

> Engineering note: each biome's hazards are good candidates for `MonoBehaviour` trigger-zone components (e.g. `SinkingSandZone`, `BlizzardController`) driven by shared timers, and each special enemy is an `EnemyData` ScriptableObject + `BaseEnemy` child controller.
