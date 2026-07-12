# Unified Enemy Roster — Implementation Notes (v1.0)

Implements the 26-enemy universal pool from *Unified Enemy Roster & Spawn Architecture v1.0* on top of the existing Section Design spawn stack (SpawnDirector / SectionEncounterSO / NetworkEnemyPool / SpawnNode). Biome Specialists (Tier 4) untouched.

## Architecture

`RosterEnemy : BaseEnemy` (`Scripts/Enemies/Base/`) is the single brain for all 28 prefabs. It adds, server-side only:

- **Attack delegation** — an optional `EnemyAttackBehaviour` component (`EnemyRangedAttack` volley or `EnemyConeAttack` breath/sweep) replaces the default melee; cadence stays in `EnemyData.AttackCooldown`.
- **Speed registries** — keyed move-/attack-speed multipliers with expiry (shriek, frenzy, enrage), plus `StatusEffectController.MoveSpeedMultiplier` integration, so **hero slows now work on roster enemies**, and `GrantSlowImmunity` windows (Screamer).
- **Events** — `OnTargetSwitched`, `OnMeleeHitLanded` for mechanic components.
- **Death interception** — `IDeathInterceptor` (Phoenix egg) and `BrainSuspended` (alive but inert).

`BaseEnemy` got exactly one addition: `protected ServerSetHealth()` for scripted revives. Nothing else changed — existing specialists/pooling/director behaviour is untouched.

Support classes: `EnemyProjectile` (server-moved, pierce option, on-hit effect, impact puddle), `EnemyHazardZone` (hostile ground puddle), `EnemyShield` (absorbing pool as `IDamageModifier` prio 250, `NetShield` NetworkVariable ready for the EnemyWorldUI shield strip — present on every roster prefab).

## Enemy → mechanic map (`Scripts/Enemies/Roster/`)

| Enemy | Component(s) |
|---|---|
| Shambler / Grunt / Fission Micro | plain RosterEnemy |
| Leaper | `OnMeleeHitEffect` → Effect_LeaperSlow (20%, 2s) |
| Vanguard Vanguardian | `FrontalShieldBlocker` (500 pool, 50%, breaks permanently) |
| Tri-Archer | `EnemyRangedAttack` ×3, 25° spread |
| Rail-Spitter | `EnemyRangedAttack` piercing *(ranged — doc's tier correction applied)* |
| Poison Weaver | ranged + Effect_RosterPoison (StackIntensity ×5) |
| Screamer | ranged + `AllyBuffShriek` (+30% MS + slow-immunity, 4s, r8) |
| Curse-Binder | ranged + Effect_CurseAntiHeal (heal ×0.4, 4s) |
| Phoenix-Ghoul | ranged ×3 + `RebirthEgg` (2.5s, 150 egg HP, once per life) |
| Alarm-Bringer | `SirenShieldOnHit` (armed / 15s, 450 self + 250 allies) |
| Spit Bile | ranged + Puddle_Acid on impact (slow, no dmg) |
| Heavy Gunner | ranged, 0.35s cadence suppression |
| Hyper-Sprinter | ranged + `KitingBehaviour` (hovers at 6.5m) |
| Ground-Slammer | `PeriodicGroundSlam` (8s, 5×3 rect, Effect_SlamStun 1.5s) |
| Big Bertha | `EnrageOnHit` (+100% AS, 10s) |
| Blood-Hound | `ChaseFrenzy` (+60% MS on retarget until hit lands) |
| Wyrmling | `EnemyConeAttack` (70°/4m fire cone / 6s, bites between) |
| Fission Spawn | existing `FissionOnDeath` chain: 1500 → 2×500 → 4×250, dmg halves / AS doubles per split (baked in the Mid/Micro data assets) |
| Sweeper-Claw | `EnemyConeAttack` (90°/3m + Effect_SweepStun, 6s **or on target switch**) |
| Stalker-Stitch | `StealthUntilClose` (r4; reveal on damage; `ServerReveal()` is the hook for Defender's **Marking**) |
| Bile-Vomiter | `EnemyConeAttack` (50°/5m + Puddle_Bile 5s damaging pool) |
| Juggernaut Blender | `SpinUpAura` (×1.15/tick, cap ×10, resets on direct hit) |
| The Greedy | `DamageBandBlocker` BlockAbove 700 + `PairedCoSpawn` → Contented (always co-spawns; doc decision #3 = yes) |
| The Contented | `DamageBandBlocker` BlockBelow 550 (not in any pool alone) |
| Void Invoker | relocated to Void (doc decision #2): `Prefabs/Enemies/Void/`, simple heavy ranged for now |

Spit Bile vs Bile-Vomiter (doc decision #1): implemented as proposed — T2 single aimed shot / small slow puddle vs T3 cone / 5s damaging pool.

## Generated assets

- `Data/Enemies/Roster/` — 27 EnemyData assets (+ `Data/Enemies/Void/ED_VoidInvoker`)
- `Data/Enemies/Effects/` — 6 EffectDataSO (slows, poison, anti-heal, 2 stuns)
- `Prefabs/Enemies/Roster/` — 27 prefabs (+ `Void/Zmb_VoidInvoker`), built from the existing Enemy.prefab template (NavMeshAgent, NetworkObject/Transform, WorldUI, SpawnEntryPresenter, StatusEffectController, DamageModifierPipeline all preserved; placeholder sprite)
- `Prefabs/Enemies/Projectile_Enemy`, `Puddle_Acid`, `Puddle_Bile`
- `Data/Enemies/Enc_Sec1` (updated: T1-heavy + T2 sprinkle), `Enc_Sec2`–`Enc_Sec4` (new) per §6 of the roster doc. T3 elites live in the **special pools** (Attention-released = "rare threat spike"; in Sec4 they're the hidden Marking targets, incl. Stalker-Stitch).

## Remaining in-editor steps (can't be done from file generation)

1. **Network Prefabs list**: add all 28 enemy prefabs + Projectile_Enemy + Puddle_Acid + Puddle_Bile.
2. **NetworkEnemyPool → extraPrefabs**: add `Zmb_FissionSpawnMid`, `Zmb_FissionSpawnMicro`, `Zmb_Contented` (spawned outside pools via ServerSpawnAt). Same list on host and client builds.
3. **SpawnDirector**: assign Enc_Sec2/3/4 next to Enc_Sec1.
4. Replace placeholder sprites/tints per enemy when art lands; optional egg visual child for Phoenix-Ghoul (`RebirthEgg.eggVisual`).
5. Defender's Marking: call `StealthUntilClose.ServerReveal()` on marked enemies.

## Known simplifications (flagged for playtesting)

- Projectiles ignore level geometry (both piercing and normal) — revisit if corridors get tight.
- Curse-Binder's "dmg-decrease" half isn't implemented — the hero-side pipeline has no outgoing-damage hook yet; anti-heal carries the identity for now.
- Screamer slow-immunity covers status-effect slows only (the only enemy slow source today).
- Leaper's lunge is approximated as extended melee reach (2.4m) + slow-on-hit; a real dash needs a displacement behaviour.
- Void Invoker is plain heavy artillery pending a telegraphed-meteor delivery.
- All numbers (HP from the roster doc; damage/cooldowns are first-pass) tune via EnemyData/prefab fields — no code changes needed.
