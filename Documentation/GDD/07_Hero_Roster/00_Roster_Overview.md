# 7. Full Hero Roster

20 heroes across 4 roles. Each hero page documents: Feature, Passive, Final Passive (Sacrifice), Skill 1, Skill 2, Ultimate.

> Engineering note: each hero is a `HeroData` ScriptableObject (name, role, difficulty, labels, base stats) paired with a `HeroController` script that inherits `BaseHero` and implements the role's shared `Feature` contract plus the hero-unique kit. See `Assets/_Project/Scripts/Heroes/<Role>/` and `Assets/_Project/Data/Heroes/<Role>/`.

## Guardians (Supports) — Feature: Teleportation/Ally interaction

| Hero | Difficulty | Labels |
|---|---|---|
| [Echo](Guardians/Echo.md) | 3/5 | ? |
| [CB](Guardians/CB.md) | 4/5 | Witch-Ancient / Catalyst |
| [TL](Guardians/TL.md) | 3/5 | Ebonwood / Tempo |
| [Kart](Guardians/Kart.md) | ?/5 | Royal / Elusive |
| [Gobluna](Guardians/Gobluna.md) | ?/5 | Fate / Siphoner |
| [Bult](Guardians/Bult.md) | ?/5 | Techno / ? |

## Barriers (Tanks) — Feature: Right-click Instant Block

| Hero | Difficulty | Labels |
|---|---|---|
| [Erdem](Barriers/Erdem.md) | 4/5 | Forsaken-CTIS / Catalyst |
| [Zga](Barriers/Zga.md) | 3/5 | Fate / Fracturer |
| [Ironworks](Barriers/Ironworks.md) | 3.5/5 | Techno-Royal / Engineer |
| [Ug](Barriers/Ug.md) | ?/5 | Witch / Captain |
| [Drago](Barriers/Drago.md) | ?/5 | Ancient / Warrior |
| [Etherborn Swordsman (Sta / Güno)](Barriers/Etherborn_Swordsman.md) | ?/5 | Etherborn-Swordsman / ? |
| [Horo](Barriers/Horo.md) | ?/5 | Techno / Warrior |

## Gladiators (DPS) — Feature: Basic Attack modifications

| Hero | Difficulty | Labels |
|---|---|---|
| [Yeliz](Gladiators/Yeliz.md) | 3.5/5 | Ronin / Warrior |
| [AP](Gladiators/AP.md) | 4/5 | Royal-Ancient / Tempo |
| [Mui](Gladiators/Mui.md) | 5/5 | Swordsman / Executioner-Elusive |
| [Etherborn Enchanter (Spark)](Gladiators/Etherborn_Enchanter_Spark.md) | 3/5 | Etherborn / Enchanter |
| [Shi](Gladiators/Shi.md) | 4.5/5 | Swordsman / Executioner |
| [Kerem](Gladiators/Kerem.md) | 2/5 | Fate-CTIS / Enchanter |
| [Bay](Gladiators/Bay.md) | 3/5 | Forsaken-Ronin / Executioner |

## Commanders (Controllers) — Feature: Roll (Dash) modifications

| Hero | Difficulty | Labels |
|---|---|---|
| [Bahadır](Commanders/Bahadir.md) | 3.5/5 | Techno-CTIS / Enchanter |
| [Ok](Commanders/Ok.md) | 3/5 | Ebonwood / Captain |
| [Barış](Commanders/Baris.md) | — | Techno-CTIS / Engineer |
| [Eub](Commanders/Eub.md) | — | ? / ? |
| [Scha](Commanders/Scha.md) | 5/5 | Swordsman / Fracturer |
