# 6. Boss Encounters: "The Family"

The boss of the Family Section is a synchronized unit of five zombies.

**Core Mechanics:** If one dies, the others get cooldown reductions. They stay together. Parents are invulnerable until all three kids are dead. Stuns pause their cooldown timers.

## Father
Emits reality-breaking waves. Slows and silences allies. Cancels mid-air abilities and turns them into "Rızık," which heals family members and resets their cooldowns.

## Mother
Teleports near all allies sequentially 4 times. Snaps her fingers (unless interrupted). A successful snap "Isolates" the ally (they see/hear only themselves). If anyone attacks her kids, she instantly snaps on them.

## First-Born
Main DPS with a soul fire sword. Always targets the ally with the highest HP %. Leaps, deals AoE damage in a rectangle, and sticks to that target. His attacks heal his siblings.

## Middle-Child
Support. Periodically uses 2 of 6 tools:
1. Damage-boosting drones for the family
2. Player-trapping cages
3. 10 explosive traps
4. Dashes to the lowest HP ally to slash
5. Fires a bazooka
6. Casts a massive frontal shield for the family

## Youngest
Throws Toyboxes. If an ally uses Skill 1 or 2 nearby, the box jumpscares (blinds), stuns, cancels the ability, and steals it. The Youngest then casts that ability on a loop. If an ally uses an Ultimate, he teleports, cancels it, casts it himself, and returns (has a cooldown).

## Reward
*Family Portrait* — Grants damage reduction scaling with how close allies are to one another.

> Engineering note: good fit for a `BossController` composed of 5 `BossUnitController` children sharing a `FamilyBossState` (cooldown-reduction propagation, invulnerability flags) — similar to a coordinator/aggregate pattern over child entities.
