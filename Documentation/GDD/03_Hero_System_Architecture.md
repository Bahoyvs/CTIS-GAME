# 3. Hero System Architecture

## Ability Structure

Every hero possesses the following kit:

- **Basic Attack (Left Click):** Primary attack (melee or ranged).
- **Roll (Shift):** Forward dash.
- **Feature:** A unique modification based on the hero's Role (e.g., Barriers get instant right-click blocks).
- **Passive:** Always active or triggered automatically.
- **Final Passive (Sacrifice):** Activated in Section 4. Kills the user but leaves a massive, permanent buff or effect for surviving allies.
- **Skill 1 & Skill 2:** Active abilities with cooldowns.
- **Ultimate:** High-impact, high-cooldown ability.

> Engineering note: this maps to an abstract `BaseHero` class exposing virtual/abstract members for BasicAttack, Roll, Feature, Passive, FinalPassive, Skill1, Skill2, Ultimate — overridden per hero controller. See `Assets/_Project/Scripts/Heroes/Base/`.

## Hero Roles

| Role | Archetype | Feature |
|---|---|---|
| **Barrier** | Tanks — damage absorption | Right-click Instant Block |
| **Gladiator** | DPS — main damage/duelists | Basic Attack modifications |
| **Commander** | Controllers — battlefield control | Roll (Dash) modifications |
| **Guardian** | Supports — healers/buffers | Teleportation/Ally interaction |

## Labels (Tags)

Each character has at least two labels: an **Origin** (who they are) and a **Class** (what they do).

- **Origins:** Witch, Ancient, Royal, Forsaken, Techno, Fate, Ronin, Ebonwood, CTIS, Swordsman, Etherborn.
- **Classes:** Catalyst, Captain, Warrior, Executioner, Tempo, Elusive (Phantom/Shade), Fracturer, Siphoner, Enchanter, Engineer.
