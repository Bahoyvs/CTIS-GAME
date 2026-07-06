# 4. The Item System (Teachers' Offices)

Players can hold a maximum of **3 items**. Items are powerful, one-time-use consumables found exclusively in Teachers' Offices. Rarity dictates respawn rates across the game.

| Item Name | Teacher / Office | Rarity | Effect |
|---|---|---|---|
| **Get Together Poster** | Erkan Hoca | Highest | Usable *only* at the start of Section 4. Instantly teleports all allies to the user's location. |
| **Debugger** | Serpil Hoca | High | Spawns only once. Revives all dead allies, heals living allies to full, and grants 1 extra life (1-up). |
| **??? (Push Aura)** | Duygu Hoca | Normal | Emits a pulse every 2 seconds for 15 seconds that pushes nearby enemies away. |
| **Perfume** | Ceren Hoca | Normal | Breaking it releases smoke that turns allies inside invisible to enemies for 10 seconds. |
| **??? (Cookie Launcher)** | Hatice Hoca | Low | Deploys a launcher that throws 12 healing cookies, distributed automatically from the lowest HP ally to the highest. |
| **???** | Leyla Hoca | Normal/Low | *Effect TBD.* |

> Engineering note: model as an `ItemData` ScriptableObject (name, teacher, rarity, respawn rate, effect description/enum, prefab ref) — see `Assets/_Project/Scripts/Data/ItemData.cs` and instances under `Assets/_Project/Data/Items/`.
