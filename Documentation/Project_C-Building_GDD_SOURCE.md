# PROJECT C-BUILDING: COMPREHENSIVE GAME DESIGN DOCUMENT

---

## 1. Core Vision & Technical Foundation

- **Genre:** 2.5D Isometric Action-Roguelite / Co-op Hero Shooter.
- **Engine:** Unity (Universal Render Pipeline - URP).
- **Art Style:** "HD-2D" / Retro 3D. 3D models and grid-based environments (1x1x1 Unity units) textured with pixel art (`Point (no filter)` and `None` compression) to maintain a crisp, nostalgic look with modern 3D lighting and depth.
- **Camera:** Orthographic isometric camera smoothly following the player, with character facing bound to the mouse cursor via raycasting.

---

## 2. Gameplay Loop & Progression

### Core Loop

The game consists of 4 distinct Sections (Chapters).

- **Sections 1–3:** Players fight enemies, survive environmental hazards, and collect "Pixel Points." Points are spent at Vending Machines to summon the Section Boss. Defeating the boss clears the section.
- **Section 4 (Final):** One hero must trigger their **Final Passive (Sacrifice)** to open the path forward. The surviving players must rush to the enemy portal core and destroy it to win the game.
- **Death Mechanic:** Dead characters remain dead until the start of the next section.

### Progression & Mechanics

- **Diamond Skill Tree:** Players gain experience and level up during the match. The skill tree is diamond-shaped with four branches (Tank, Damage, Control, Support). Cross-picking is highly encouraged (e.g., Supports picking Damage upgrades).
- **Portal Challenges:** Occasional portals appear. Entering them sends players to a different zone for a challenge. Completing it grants rewards, and players are returned to the exact spot they left.

---

## 3. Hero System Architecture

### Ability Structure

Every hero possesses the following kit:

- **Basic Attack (Left Click):** Primary attack (melee or ranged).
- **Roll (Shift):** Forward dash.
- **Feature:** A unique modification based on the hero's Role (e.g., Barriers get instant right-click blocks).
- **Passive:** Always active or triggered automatically.
- **Final Passive (Sacrifice):** Activated in Section 4. Kills the user but leaves a massive, permanent buff or effect for surviving allies.
- **Skill 1 & Skill 2:** Active abilities with cooldowns.
- **Ultimate:** High-impact, high-cooldown ability.

### Hero Roles

| Role | Archetype | Feature |
|---|---|---|
| **Barrier** | Tanks — damage absorption | Right-click Instant Block |
| **Gladiator** | DPS — main damage/duelists | Basic Attack modifications |
| **Commander** | Controllers — battlefield control | Roll (Dash) modifications |
| **Guardian** | Supports — healers/buffers | Teleportation/Ally interaction |

### Labels (Tags)

Each character has at least two labels: an **Origin** (who they are) and a **Class** (what they do).

- **Origins:** Witch, Ancient, Royal, Forsaken, Techno, Fate, Ronin, Ebonwood, CTIS, Swordsman, Etherborn.
- **Classes:** Catalyst, Captain, Warrior, Executioner, Tempo, Elusive (Phantom/Shade), Fracturer, Siphoner, Enchanter, Engineer.

---

## 4. The Item System (Teachers' Offices)

Players can hold a maximum of **3 items**. Items are powerful, one-time-use consumables found exclusively in Teachers' Offices. Rarity dictates respawn rates across the game.

| Item Name | Teacher / Office | Rarity | Effect |
|---|---|---|---|
| **Get Together Poster** | Erkan Hoca | Highest | Usable *only* at the start of Section 4. Instantly teleports all allies to the user's location. |
| **Debugger** | Serpil Hoca | High | Spawns only once. Revives all dead allies, heals living allies to full, and grants 1 extra life (1-up). |
| **??? (Push Aura)** | Duygu Hoca | Normal | Emits a pulse every 2 seconds for 15 seconds that pushes nearby enemies away. |
| **Perfume** | Ceren Hoca | Normal | Breaking it releases smoke that turns allies inside invisible to enemies for 10 seconds. |
| **??? (Cookie Launcher)** | Hatice Hoca | Low | Deploys a launcher that throws 12 healing cookies, distributed automatically from the lowest HP ally to the highest. |
| **???** | Leyla Hoca | Normal/Low | *Effect TBD.* |

---

## 5. Environments & Biomes (C-Building Mutations)

The C-Building has fractured into distinct dimensional biomes, each featuring unique hazards and specialized enemies.

### 5.1 Forest Section

An arboreal temple where vines and fungi have overtaken the concrete.

**Hazards**
- *Balance Meter:* Swinging on vines/roots drains balance; falling resets you.
- *Vine Webs:* Root players for 2s unless "Pruned" by teammates.
- *Spore Pockets:* Hallucinogenic spores stun (0.5s) and cause "Sporevision" HUD distortion for 5s. Cleared by carrying Lanterns.
- *Sunbeam Clearings:* Grants "Photosynthesis" (slow heal) and repels minor Feral Roots.
- *Animal Echoes:* Staying silent (no sprint/interact) for 10s grants stealth.

**Special Enemies**
- *Shaman Woman (520 HP):* Infinite range. Deals no damage but continuously stuns random allies for 0.25s. Interrupts/cancels casting skills (does not reduce Frostbite bar).

### 5.2 Void Section

The building is ripped into orbit, with exposed corridors drifting in zero gravity.

**Hazards**
- *Zero-G Corridors:* Momentum-based movement. Must use "Magnet Brake" (crouch) to avoid impact damage.
- *Vacuum Breaches:* Every 90s, a corridor ruptures. Players have 5s to don visors (skill key) or take heavy damage over time.
- *Electrostatic Conduits:* Touching arcs stuns for 1s and drains oxygen by 10%.
- *Debris Showers:* Floating chunks that can be ridden with "Magnet Boots" or crush players who miss timing.

### 5.3 Frozen Section

A crystalline ice keep of frosted marble.

**Hazards**
- *Cracking Ice Floors:* Repeatedly walking on fragile frost breaks it, causing fall damage to lower floors.
- *Blizzard:* Happens twice per section (30–45s). Shrinks vision, reduces move speed, cancels mid-air projectiles, shortens ability ranges, increases cooldowns, and muffles audio.
- *Frostbite Bar:* Fills over time/when not using abilities. If full, the ally freezes (Stunned in an ice shell). Freezing resets skill/ult cooldowns but increases the next cast's cooldown.
- *Falling Icicles:* Drop periodically (with a cracking audio cue), dealing heavy damage. Lower floors have more icicles.

**Special Enemies**
- *Ice Archer (830 HP):* First hit freezes the ally (Ice shell).
- *Troll (2000 HP):* Heavy melee; applies stacking anti-heal.
- *Froster (650 HP):* Ranged; slows on hit. Fortifies ice shells if hitting a frozen ally.
- *Behemoth (3600 HP):* Massive melee; attacks cause shockwaves that crack ice. If they break ice, their attack cooldown resets.
- *Glacial Wolf (1300 HP):* Gains double speed/damage against isolated players. Ignores ice shells and deals double damage to frozen targets.

### 5.4 Desert Section

A sun-baked outpost half-buried in dunes.

**Hazards**
- *Sinking Sands:* Standing still causes sinking. Waist-deep requires Spacebar spam. Neck-deep requires intense spam. Swallowed equals 10s disappearance and returning with critical HP.
- *Sandstorms:* Happens twice per section (30–45s). Reduces vision/movement, disables item use, prolongs CC effects, and spawns more Bandits.
- *Sunburn Bar:* Builds over time/when using abilities. Decays after 10s of no ability use. If full, vision blurs and damage taken increases significantly.
- *Mirage Traps:* Fake items/pickups that are non-interactable until attacked or walked over.

**Special Enemies**
- *Bandit (900 HP):* Invisible until hit by a skill. Steals an item, deals damage, and dashes away. If alive after 5s, the item is deleted permanently.
- *Highwayman (1300 HP):* Steals Pixel Points. Damaging them drops points; killing them recovers all points.
- *Sphinx (1900 HP):* Elite tank. Appears with an invulnerable mini-hurricane that deals AoE damage. Hurricane recharges periodically.
- *Scarab (1200 HP):* Applies stacking Poison TIME (not damage). On death, bursts into tiny scarabs (80 HP) that also poison.
- *Scorpion (300 HP):* Fast skirmisher; basic attack is a double hit.
- *Mummy (700 HP):* Throws wrappings that spawn flies. Flies distort audio and deal DoT (stacks).
- *Desert Worms (2600 HP):* Untargetable ambushers. Erupt from sinking sands/sandstorms, crashing and damaging players above.
- *Prophet (550 HP):* Support. Summons wind tunnels over the 4 highest-HP enemies, healing them.

### 5.5 Family Section

A cozy, old house hiding a dark, corrupted family.

**Hazards**
- *Watcher Paintings:* Staring too long grants a "Mark of Guilt." Marked players take more boss damage and attract whispering "Family Echoes."
- *Looping Halls:* Halls that loop infinitely unless the whole team walks backwards simultaneously.
- *Toybox Scatters:* Randomly spawned boxes that laugh, explode, blind, and stun (0.5s) if approached.
- *Room Lock + Exit Displacement:* Entering a room slams the door. Exiting might teleport the player elsewhere in the building.

---

## 6. Boss Encounters: "The Family"

The boss of the Family Section is a synchronized unit of five zombies.

**Core Mechanics:** If one dies, the others get cooldown reductions. They stay together. Parents are invulnerable until all three kids are dead. Stuns pause their cooldown timers.

- **Father:** Emits reality-breaking waves. Slows and silences allies. Cancels mid-air abilities and turns them into "Rızık," which heals family members and resets their cooldowns.
- **Mother:** Teleports near all allies sequentially 4 times. Snaps her fingers (unless interrupted). A successful snap "Isolates" the ally (they see/hear only themselves). If anyone attacks her kids, she instantly snaps on them.
- **First-Born:** Main DPS with a soul fire sword. Always targets the ally with the highest HP %. Leaps, deals AoE damage in a rectangle, and sticks to that target. His attacks heal his siblings.
- **Middle-Child:** Support. Periodically uses 2 of 6 tools: 1) Damage-boosting drones for the family, 2) Player-trapping cages, 3) 10 explosive traps, 4) Dashes to the lowest HP ally to slash, 5) Fires a bazooka, 6) Casts a massive frontal shield for the family.
- **Youngest:** Throws Toyboxes. If an ally uses Skill 1 or 2 nearby, the box jumpscares (blinds), stuns, cancels the ability, and steals it. The Youngest then casts that ability on a loop. If an ally uses an Ultimate, he teleports, cancels it, casts it himself, and returns (has a cooldown).

**Reward:** *Family Portrait* — Grants damage reduction scaling with how close allies are to one another.

---

## 7. Full Hero Roster

### 7.1 Guardians (Supports)

#### Echo — *Difficulty: 3/5 | Labels: ?*
- **Feature (Track Switch):** Right-click instantly swaps between Bass, Melody, and Tempo tracks. No cooldown. Overwrites all abilities.
- **Passive (Beat Overdrive):** Switching tracks or casting skills grants stacks (Max 5). At 5 stacks, unleashes a global pulse granting allies the current track's buff at 50% potency for 3s. Stacks reset.
- **Final Passive (Eternal Mix):** On sacrifice, imprints a permanent 6m aura on surviving allies pulsing every 3s. Allies can manually switch their personal track (Bass: 15% DMG reduction, Melody: 2% max-HP heal, Tempo: +10% Attack Speed).
- **Skill 1 (Drop Zone):** Bass (8m zone, DMG reduction + ATK speed, stuns on detonate); Melody (Heals max HP, reduces enemy healing); Tempo (Allies fire mini-AoE pulses with basic attacks).
- **Skill 2 (Toggle & Supercharge):** Instantly switch tracks, refresh Drop Zone duration, grant allies a surge of the new buff, and lightly knockback enemies.
- **Ultimate (Live Set):** Channels for 2s to create a 12m mega-Drop Zone cycling through Bass (Stun+DMG), Melody (Heal), and Tempo (Ability haste + Silence). Zone remains for 5s on the finished track.

#### CB — *Difficulty: 4/5 | Labels: Witch-Ancient / Catalyst*
- **Feature:** Spawns spikes around herself when guarding an ally, damaging nearby enemies.
- **Passive (Faith Ally):** Selects one ally per section. Ally starts weak. At 50% section completion, their stats double. At 75%, fully healed, cooldowns reset, and all abilities automatically cast twice (Ults cast twice only once).
- **Final Passive:** Allies gain a Faith Bar from dealing/taking damage. When full, they heal and enter "Second Faith Form" (acting as a Faith Ally temporarily).
- **Skill 1:** Grants a massive shield that slowly drains the ally's health. If the ally kills 5 enemies while shielded, they recover the lost health. Lasts 20s.
- **Skill 2:** Neutralizes an ally for 20s and creates an invulnerable "Spirit Ally" clone that can attack/cast. The real body remains vulnerable to damage and CC.
- **Ultimate:** Cleanses all debuffs globally. Grants invulnerability to herself and the Faith Ally. Drains health from nearby enemies and distributes it to the team (excluding Faith Ally).

#### TL — *Difficulty: 3/5 | Labels: Ebonwood / Tempo*
- **Feature:** Buffed ally receives increased healing from all sources.
- **Passive:** Has a massive circle. Hitting the same enemy 3 times increases damage and adds lifesteal + thorns. At 7 hits, provides continuous AoE healing. Switching targets breaks the combo.
- **Final Passive:** Allies heal themselves and the nearest ally after hitting the same target 3 times.
- **Skill 1 (2 Charges):** Throws a piercing thorn. If it hits a wall, it stays longer, stuns, and deals bonus damage. If no wall, it roots at max range. TL can attack the thorn to deal AoE damage and heal allies. If enemies inside die, grants massive heal/shield.
- **Skill 2:** Unleashes 8-directional vines that briefly stun enemies.
- **Ultimate:** Takes flight (invulnerable). Basic attacks target allies, granting heal + shield, while slowing/damaging nearby enemies. Cannot use other skills.

#### Kart — *Difficulty: ?/5 | Labels: Royal / Elusive*
- **Feature:** Turns himself and an ally invisible; their next attack hits three times.
- **Passive:** Uses a 52-card deck (Clubs, Hearts, Spades, Diamonds). Basic attacks throw 3 random bouncing cards. Reloading grants move speed and resets feature CD. Cards have varied effects (Clubs: Slows enemies / speeds allies; Hearts: AoE DMG / DMG boost; Spades: Disarms enemies / reduces ally CDs; Diamonds: Stuns / grants stun-shields). Kills on affected enemies trigger powerful secondary effects.
- **Skill 1 (3 Charges):** Drops 4 cards (one of each suit) on random enemies.
- **Skill 2:** Consumes 5 cards, deploys a clone, jumps back, turns invisible, and cleanses slows. Clone explodes after taking damage, firing the 5 cards and re-triggering invisibility.
- **Ultimate:** Pulls missing cards to complete the deck + adds 52 more. Gains extreme move speed and rapid-fires all cards at allies and enemies.

#### Gobluna — *Difficulty: ?/5 | Labels: Fate / Siphoner*
- **Feature:** Jumps to an ally, creating a lingering healing circle.
- **Passive:** Dealing damage heals nearby allies scaling with the damage dealt.
- **Final Passive:** Enemies are permanently on green fire.
- **Skill 1:** 1s Cooldown. Fires 3 piercing darts that damage enemies and heal allies.
- **Skill 2:** No Cooldown. Sets enemies in a frontal cone on green fire (permanent chip damage). Reactivatable only if no enemies are on fire, OR when a special bar fills (filled via healing), allowing her to stun all burning enemies.
- **Ultimate:** Sends a blast to the nearest ally that bounces globally for 18s. Bounces occur every 5s, fully healing Gobluna. During the 18s, Skill 1's CD becomes 0.4s.

#### Bult — *Difficulty: ?/5 | Labels: Techno / ?*
- **Feature:** Bult and the jumped ally gain a "Friend Bot."
- **Passive:** When a bot reverts, its associated skill CD resets, granting Bult move speed and a shield. Max X bots allowed.
- **Skill 1 (4 Charges):** Enemy target: Attaches Hostile Bot (reveals, chip damage, reduces damage output, grants move speed to allies moving toward them; kill grants big heal). Ally target: Attaches Friend Bot (heals, boosts damage against Hostile Bot targets).
- **Skill 2 (3 Charges):** Plants a Mine Bot. Ally trigger: Creates a healing zone and grants Range Bots (bonus range, reactive damage). Enemy trigger: Crushes enemies and attaches Stun Bots (stuns upon taking damage).
- **Ultimate:** Gains a Beam Bot. All active bots connect via lasers that heal allies and damage/slow enemies. If a bot reverts during the ult, it explodes, dropping a speed/heal shimmer and extending the ult duration.

### 7.2 Barriers (Tanks)

#### Erdem — *Difficulty: 4/5 | Labels: Forsaken-CTIS / Catalyst*
- **Feature:** Instant right-click block; heals whether damage is blocked or not. Fights with companion "Garı." Garı also blocks and heals Erdem.
- **Passive:** No ability cooldowns, but skills cost % Max HP followed by a 1s global delay. Garı mirrors attacks and shares incoming damage. Abilities change if Garı is attached or detached.
- **Final Passive:** Spawns expanding barriers centered on allies that push enemies out. Inside, allies permanently gain +30% Max HP. Zones can overlap.
- **Skill 1:** *With Garı:* Spanks Garı into enemies for AoE stun. *Without Garı:* Grabs butcher sword (ATK buff) and invincible-dashes to Garı.
- **Skill 2:** *With Garı:* Sends Garı to an ally (ally shares Erdem's passive). *Without Garı:* Sends Garı to a location, pushing and stunning zombies along the path.
- **Ultimate:** Garı recalls at lightspeed, pushing all zombies in a massive circle into a permanent barrier. Allies inside gain Max HP; Erdem's Max HP permanently doubles.

#### Zga — *Difficulty: 3/5 | Labels: Fate / Fracturer*
- **Feature:** Successful right-click block turns the next basic attack into an AoE ground punch that crashes enemies. Can leap to allies to instant-block for them.
- **Passive:** Weapon has 3 parts (Cannonball, Chain, Body). Very slow AoE attacks. Cannonball must return to attack again. 4th attack is a wide cone. Attacks stun (0.25–0.5s) and heal Zga (2–10% Max HP).
- **Final Passive:** Dismantles weapon permanently, granting surviving allies the weapon part passives.
- **Skill 1:** Hold to charge (up to 5s). Slams ground to stun. Longer charge = bigger radius. >4s charge applies heavy "Crash" slow after the stun.
- **Skill 2:** Detaches cannonball and moves on a chain (cannot attack). Reactivate to grab/root zombies on the chain. At the end of the duration, Zga snaps back to the cannonball, dragging and applying a 90% slow to captured zombies.
- **Ultimate:** Gains 15–20% HP shield, dismantles weapon, and gives parts to allies for 15–20s (Body = ATK buff; Chain = skills stun/crash; Cannonball = MS aura + periodic healing). Zga fights bare-handed, gaining Max HP with every rapid punch. Calls parts back when ended.

#### Ironworks — *Difficulty: 3.5/5 | Labels: Techno-Royal / Engineer*
- **Feature:** Right-click grants an instant personal mini-shield.
- **Passive (Adaptive Shield):** Restores 20% max shield every 6s. If full and damaged, 10% of overflow damage converts to AoE healing.
- **Final Passive (Field Drone Armada):** Unleashes 3 Field Drones that latch onto allies. Emits a 6m aura (1% max HP heal/sec + 5% move speed) permanently.
- **Skill 1 (Portable Cover):** Tosses a deployable 8s Hex-Shield blocking projectiles and granting +30% DMG reduction to allies behind it. Can be retrieved.
- **Skill 2 (Explosive Arc Trap):** Plants an Arc Mine. After 1.5s, projects an electric wall (6s) that damages and slows (40%). Can be picked up to reset CD.
- **Ultimate (Fortress Fabrication):** Channels 2s to build a Mobile Siege Rig (10s duration). Rotates and fires knockback blasts. Allies within 8m gain stacking "Repair Field" (up to 15% HP/sec). Reactivate to detonate for massive AoE damage and grant a 25% Max HP shield to the team.

#### Ug — *Difficulty: ?/5 | Labels: Witch / Captain*
- **Feature:** Successful blocks stack an extra jump.
- **Passive:** Holding Space allows floating.
- **Final Passive:** Allies gain double jump. Double jumping emits a push wave.
- **Skill 1:** Tap: Shield scales on missing HP + AoE ATK speed buff. Hold: Dash (distance scales with hold time). After cast, attacks open an umbrella to push enemies in a rectangle.
- **Skill 2:** Opens an infinite-HP barrier in front. Allies behind it heal. Ug is locked out of skills/feature and uses ranged attacks. Closes after Xs or reactivation.
- **Ultimate:** Creates a massive wind tunnel that continuously pushes enemies out. Allies inside gain a continuously growing shield.

#### Drago — *Difficulty: ?/5 | Labels: Ancient / Warrior*
- **Feature:** Successful block flutters his dragon, grants instant MS, and "Corks" the nearest enemy (Corked enemies get stunned when taking damage).
- **Passive:** Heals while walking based on MS. Receiving healing grants MS. Hitting the MS cap converts excess MS into AoE shields.
- **Skill 1:** Dragon breathes fire in a cone while moving. Deals low damage but Corks enemies hit for 1s.
- **Skill 2:** Dragon spins around Drago in a growing circle. Drago moves slowly, cannot attack/cast, but takes reduced damage. Enemies inside take damage and are pulled toward the center. Stunned enemies take bonus damage. Ends via time, HP threshold, or recast, dealing burst damage/shields based on duration.
- **Ultimate:** Ascends into the sky (MS cap removed, incredible speed). Descends on activation, dealing AoE damage and shielding allies on impact. Doubles the duration/range of the next Skill 1 & 2 casts.

#### Etherborn Swordsman (Sta / Güno) — *Difficulty: ?/5 | Labels: Etherborn-Swordsman / ?*
- **Feature:** Successful block reduces skill/ult CDs (by 2s) for himself and all shielded allies.
- **Passive:** Inactive for 7s triggers meditation. Heals himself and projects a circle granting allies scaling DMG buffs and shields every 2s.
- **Skill 1:** Grants a shield to himself and an ally. If broken, the shield reappears once.
- **Skill 2:** Enemy target: Dashes, stunning enemies in path, deals damage/crashes target. Next attack is AoE. Ally target: Dashes faster, stuns enemies, shields ally. Enters a 5s stance where feature blocks become a large half-circle sword sweep (0.5s CD).
- **Ultimate (2 Charges):** Creates 3 large sun spots for 10s. Allies inside get max passive damage buff, 1s tick shields, and heal upon casting any skill.

#### Horo — *Difficulty: ?/5 | Labels: Techno / Warrior*
- **Feature:** Successful block shields herself and the nearest ally (scaling with blocked damage) and reduces Skill 2 CD.
- **Passive:** Every 2nd basic attack is a half-circle kick that pushes enemies.
- **Final Passive:** Permanent hologram copies for everyone.
- **Skill 1:** Dash + damage reduction. Second cast deals AoE damage and fires a taunt projectile.
- **Skill 2 (3 Charges):** Creates a Hologram Copy next to a target enemy. Copy auto-attacks, executes Skill 1 & Ult in tandem with Horo, shares passives, and inherits shields. Max 5 active.
- **Ultimate:** Reflects 50% of incoming damage back to the source for a duration. Upon ending, gains Max HP scaling with the reflected damage.

### 7.3 Gladiators (DPS)

#### Yeliz — *Difficulty: 3.5/5 | Labels: Ronin / Warrior*
- **Feature:** Basic attacks are rectangular. Double-clicking changes the attack to a wider, fan-shaped strike. Both attacks slow enemies (second one slows more).
- **Passive:** Hitting slowed enemies grants MS and heal. Fan-shaped attacks block enemy attacks and mark them as "Stabbed."
- **Final Passive:** Allies' basic attacks have a 15% chance to fire a blocking, slowing mini-fan wave.
- **Skill 1:** Throws two fans in hypnotic whirlpool paths. CD resets if the second basic attack hits 3+ enemies.
- **Skill 2:** Opens a large fan in one cardinal direction for 1s, blocking all damage to heal. Then strikes the other 3 directions sequentially.
- **Ultimate (3s):** Throws closed fans at all nearby enemies. After 3s, fans open automatically into the secondary fan strike. If the enemy was "Stabbed," the effect chains to new targets.

#### AP — *Difficulty: 4/5 | Labels: Royal-Ancient / Tempo*
- **Feature:** Attacks grant Royalty Points. Attack speed increases infinitely per 10 points. Attacks evolve per section (Sec 1: Single target. Sec 2: +2 closest enemies. Sec 3: +3 furthest enemies).
- **Passive:** HP <15% spawns Royal Knights pushing enemies away and granting invincibility. Resets per section.
- **Final Passive:** Fatal damage to an ally is blocked by a Royal Knight springing from their back. Knight stays to fight. Works once per ally.
- **Skill 1:** Fires a marking projectile (grants minor shield). Grants massive ATK speed against the marked target.
- **Skill 2:** Summons Royal Horse for movement speed and run-and-gun capability. Dismounting early refunds CD. Ends with a small heal.
- **Ultimate:** Drops crossbow as an auto-turret. AP channels a continuous, aiming beam of ancient sorcery dealing heavy AoE damage, emitting push waves from his body.

#### Mui — *Difficulty: 5/5 | Labels: Swordsman / Executioner-Elusive*
- **Feature:** Half-circle melee slashes.
- **Passive:** Running uninterrupted for 3s grants MS stacks (Max 3). At max stacks, lethal attacks cause Mui to phase behind the attacker and dodge. Gains +25% Ghost effect (passes through units). Damage dealt adds up to 50% Ghost effect.
- **Skill 1:** Executes enemies <5% HP. Jumps to a location, then instant-jumps to mouse cursor, delivering a front/back half-circle slash combo.
- **Skill 2:** Creates a misty area turning him invisible. Next attack phases to target. Invisibility pulses on/off. Adds invulnerability to chip damage <5% HP during Ult.
- **Ultimate:** Upgrades kit. Gains 50% Ghost effect, instant max running stacks. Stopping/attacking doesn't break momentum. Heavy attacks trigger auto-dodges, levitation, and prompt a follow-up phase attack. Skill 1 leaves a persistent damage zone and executes <10% HP.

#### Etherborn Enchanter (Spark) — *Difficulty: 3/5 | Labels: Etherborn / Enchanter*
- **Feature:** Attacks aren't projectiles; they are sparks that spawn at the cursor and explode after a delay.
- **Passive:** Colliding damage sources (projectiles/skills) trigger an explosion scaling with combined damage. If DMG > X, creates a Black Hole that pulls enemies.
- **Final Passive:** Every 4th projectile collision triggers the Black Hole effect.
- **Skill 1:** Sequential casts. 1: Slow right-curve. 2: Medium left-curve. 3: Fast linear projectile.
- **Skill 2:** Pulls nearby enemies, jumps backwards, and deals delayed AoE damage at the original spot.
- **Ultimate:** Quick jumps forward. Fires 3 projectiles toward the cursor sequentially. Repeats 6 times every 3s from the initial casting point.

#### Shi — *Difficulty: 4.5/5 | Labels: Swordsman / Executioner*
- **Feature:** Marks target. Basic attack dashes to target, pins blade into them, and injects poison periodically. Detaching grants MS.
- **Passive:** Low ATK speed/Max HP. All damage injects permanent Poison stacks (chip DMG, slow, DMG reduction). Every 5th hit from a poisoned target stuns them. If Poison Stacks = Current HP, Instant Death (Execution). Drops poison in an AoE upon death.
- **Skill 1 (2 Charges):** Jump. Hitting a wall roots for 2s but buffs next attack range. No wall = 50% CD refund. Resets basic attack.
- **Skill 2 (2 Charges):** Levitates (invulnerable to damage). Can drop down with a basic attack that spins around the enemy for 2s, dealing massive poison. Resets basic attack.
- **Ultimate:** Vanishes and reappears. Projects a massive moving aura. Enemies inside rapidly gain poison; allies gain ATK speed/lifesteal. Executing a target inside spreads their poison to nearby enemies.

#### Kerem — *Difficulty: 2/5 | Labels: Fate-CTIS / Enchanter*
- **Feature:** Wide crescent attacks.
- **Passive:** Skills stack up to 10 charges. Killing a unit instantly recovers skill charges.
- **Final Passive:** Slain enemies explode, dealing AoE damage.
- **Skill 1:** Throws an AoE power-ball. If >4 skill stacks, Kerem gains MS and can run-and-gun.
- **Skill 2:** Pulls enemies into a line, then pushes them away, stunning on impact.
- **Ultimate:** Jumps to a location, leaving a burning trail and a fire zone. Enters Ultimate Mode: all attacks/skills leave fire. Fills a secondary bar. When full, reactivate for a massive push/AoE explosion.

#### Bay — *Difficulty: 3/5 | Labels: Forsaken-Ronin / Executioner*
- **Feature:** Every 5th attack/skill transforms basic attack into a spinning AoE strike.
- **Passive:** All damage applies Bleed. Bleeding enemies instantly die if HP <10%.
- **Final Passive:** All enemies permanently bleed upon taking any damage.
- **Skill 1:** Kills reset CD. Instant jump. If an enemy is near, homes in and strikes, granting stacking ATK speed. Maxes out the spin-attack stack.
- **Skill 2:** Fires a fast, infinite-range piercing blade (0 CD, recastable when destroyed). Using Skill 1 while the blade is mid-air teleports Bay to the blade to perform Skill 1.
- **Ultimate:** Targets lowest-HP enemy, applies Bleed, and immune-slashes them for 1–1.5s. Kills reactivate the ultimate for chain-executions. Executions heal Bay for 20% Max HP.

### 7.4 Commanders (Controllers)

#### Bahadır — *Difficulty: 3.5/5 | Labels: Techno-CTIS / Enchanter*
- **Feature:** Turns invisible, gains MS, and stuns enemies he passes through.
- **Passive:** Not casting for 5s (or standing near an ally) grants an MS aura.
- **Final Passive:** Killing a zombie reduces ally ability CDs by 2s and grants MS.
- **Skill 1:** Form 0: Throws a "0" that stuns and glitches enemies. Double-clicking rides the "0." Form 1: Throws a "1" to stab/slow an enemy. Glitched enemies given Spyware turn into bodyguards. Stabbing an enemy with Spyware simultaneously stabs all Spyware carriers.
- **Skill 2 (4 Charges):** Uploads Spyware (increases damage taken). Enemy death returns Spyware and reduces all CDs by 1s. Kills while holding Spyware grant extra charges.
- **Ultimate:** Hacks a spawn area (must channel). Once hacked, all enemies spawning from it automatically carry Spyware.

#### Ok — *Difficulty: 3/5 | Labels: Ebonwood / Captain*
- **Feature:** Ok and bonded allies instantly fire a basic attack after casting a skill and shoot while rolling.
- **Passive:** Every 5s, enemies under 20% HP in a massive radius are disarmed and 99% slowed.
- **Final Passive:** Basic attacks become double instant shots that blind for 0.5s.
- **Skill 1:** Explodes a light arrow on the bow, blinding enemies and illuminating the area. Next 4 attacks stun and blind. Next attack becomes 3 instant shots.
- **Skill 2:** Unbonded: Tethers to allies (heals/shields). Bonded: Pulls allies behind Ok, cleanses debuffs, and applies her Skill 1 attack buffs to them.
- **Ultimate:** Fires an arrow into the sky triggering a massive rapid arrow rain (scales with remaining enemy HP). Bonded allies echo this attack. Extremely low CD.

#### Barış — *Difficulty: — | Labels: Techno-CTIS / Engineer*
- **Feature:** —
- **Passive:** Omnitrix watch offers 1 of 3 upgrades (DMG, HP, CD, Speed) every 10 minutes.
- **Final Passive:** Everyone permanently gains Barış's collected upgrades.
- **Skill 1:** Gravitational push/dash. Ping enemies = pushes them away. Ping ground = dash. (Upgrades: Teleportation, Invulnerability during dashes, Lifesteal from enemy movement).
- **Skill 2:** Blasts an X shape that leaves a burn/slow trace. (Upgrades: Instant explosions, teleports enemies hit 3 times).
- **Ultimate:** Fuses with watch, fires a forehead beam, activates all X traces, and temporarily completes 1/2 of his ability upgrades.

#### Eub — *Difficulty: — | Labels: ? / ?*
- **Feature:** Has 4 Elemental Forms (Terra, Aqua, Aer, Ignis) altering all skills. Starts Terra. Ulting cycles form. Casting skills generates Elementum points. HP <15% grants a 30% shield.
- **Final Passive:** Lands, whirlpools, clouds, and volcanos spawn randomly across the map.
- **Skill 1:** Terra: Creates permanent rectangular land (buffs allies, damages/crashes enemies). Aqua: Shields allies or throws slowing waterballs if near a whirlpool. Aer: Cloud levitates enemies (unbreakable stun). Ignis: *TBD*.
- **Skill 2:** Terra: Earthquake damages/slows (interacts with land). Aqua: *TBD*. Aer: ATK speed buff, attacks pull Eub to target, ends with AoE lightning. Ignis: *TBD*.
- **Ultimate:** Cycles form. Terra: Attaches a Land zone to an ally. Aqua: Bubbles an enemy and nearby enemies. Aer: Storm drags all stunned/crushed enemies into the center for massive damage. Ignis: Tri-beam fire laser. *Elementalis Form (Secret 5th Form):* Ult uses Elementum points to nuke the screen, granting a massive shield before returning to Terra.

#### Scha — *Difficulty: 5/5 | Labels: Swordsman / Fracturer*
- **Feature:** Basic attacks against charging enemies bump them back, canceling the charge and stunning them. Next attack is a crescent cleave.
- **Passive:** Split scissors = MS buff. Reassembling = Shields for team. Dashing through allies grants them shields.
- **Skill 1:** Dash. Temporarily splits scissors if assembled.
- **Skill 2 (3-Step Combo):** 1) Throws half-scissor. Pulls to it or pulls enemies. 2) Throws second half. Pulls all enemies to Scha in a spin, emits sonic burst. 3) Frontal triangle cleave.
- **Ultimate (6 Stacks):** Casts a large circular zone bordered by her split scissors (enemies inside take bonus DMG/Slow). Scha goes invisible + MS buff. Reassembling scissors crushes enemies, shields allies, and detonates the zone. Ends when timer runs out or manually closed via skill/ult recast.

---

*End of Document — Project C-Building GDD*
