# Enemy scaling refactor — design

**Date:** 2026-09-29
**Repos:** ARPGEnemySystem (owner), ARPGCharacterSystem (consumers), BalanceLab (mirror + guard test)
**Status:** approved in conversation; awaiting review of this written spec

## 1. Problem

Enemy scaling is written into vanilla's `npc.damage`, `npc.defense`, `npc.lifeMax` once: at `BossManager.OnSpawn` for bosses and on the first `NPCManager.PreAI` for everything else. Every one of the gaps below follows from that "write once and hope" model, plus the two managers being hand-maintained copies of the same idea.

1. **Vanilla AI overwrites damage/defense mid-fight.**
   - Every frame from spawn: Skeletron, Skeletron Prime, Plantera, Duke Fishron, Empress of Light.
   - After a phase change: EoC phase 2, both Twins in phase 2, Cultist at ≤50% HP, Queen Bee (Expert), Moon Lord head/hand in the eye-destroyed state.
   - Regular enemies too: fighter AI (`AI_003`), The Hungry, aiStyle 39, Mothron, Big Mimic.
   - All writes are absolute constants or derived from `defDamage`/`defDefense`. Every relative write (`*=`, `+=`) follows a same-frame reset (NPC.cs survey, 2026-09-29).
2. **`SetDefaults` re-runs on living NPCs wipe all per-NPC mod data.** tModLoader re-creates every `GlobalNPC` instance (GlobalLoaderUtils.cs:43-53). It happens through two paths:
   - `NPC.Transform`, 35 vanilla call sites, including wall spiders on every wall grab (164↔165, 236↔237, 163↔238, 239↔240, 530↔531);
   - the EoW segment conversion (NPC.cs:54893-54914).

   Both call `NPC.SetDefaultsKeepPlayerInteraction` (NPC.cs:3459). The consequences:
   - On the server, the regular-enemy path re-rolls level, rarity and modifiers and its first-frame pass sets `life = lifeMax`, so a transforming spider heals to full.
   - On clients the level is 0, so scaling vanishes.
3. **Multiplayer.** `NPCLoader.OnSpawn` runs only inside `NPC.NewNPC` (NPC.cs:91638), which runs on the server or in single player.
   - Contact damage (`Player.Update_NPCCollision`, local player only) and hit defense (`GetIncomingStrikeModifiers`) are computed on clients.
   - The NPC packet does not carry damage or defense.
   - Clients see scaled values only when a mod sync packet happens to arrive, and the AI rewrites them anyway.
4. **Two combat rules.** `NPCManager.ModifyIncomingHit` zeroes vanilla's defense step for regular enemies. `BossManager` has no such hook, so bosses pay vanilla `defense/2` **and** the mod's resistance: defense counts twice.
5. **Boss-fight membership keyed on the `boss` flag.** EoW segments, the Destroyer body, Skeletron/Prime arms and Golem parts are not flagged and get regular-enemy rolls. Moon Lord's head and hands are flagged. A Twin clears its flag when it dies first.
6. **Enemy projectiles are not level-scaled.**
   - Commit `9c1b087` (2026-04-30) accidentally removed projectile level scaling. Vanilla builds 181 of 182 enemy projectile damages from fixed numbers, not `npc.damage`.
   - The CS hurt pipeline also drops vanilla's hostile ×2 and the Expert/Master multiplier, so projectiles land at ¼ (Expert) or ⅙ (Master) of vanilla.
7. **Vanilla defense reducers do nothing.** Ichor (−15), Betsy's Curse (−40), and vanilla flat and percentage armor pen all act through the vanilla defense step, which is zeroed. The mod's resistance reads the raw `npc.defense`.
8. **Negative defense is undefined.**
   - `75·d/(d+60)` has a pole at −60 and flips sign below it, so armor pen is floored at 0 (TargetResistances.cs:95) and stops paying.
   - Elemental resistance already goes negative, which is inconsistent.
   - Player-side resistances are floored at 0 in `PlayerHurtPipeline`.
9. **Direct readers of the raw fields:**
   - `PlayerHurtPipeline` (contact damage);
   - `TargetResistances` and `ElementalDamageCalculator`, and through them `ReapResolver`;
   - `XPAwardGlobalNPC`;
   - `NPCTooltip`.

   Each repeats a "try NPCManager, else BossManager" branch.

## 2. Goals

1. Every combat read of an enemy's damage or defense returns the scaled value, whoever last wrote the vanilla field.
2. Scaling survives every `SetDefaults` re-run: same profile, same health fraction, no re-roll, no heal.
3. One pipeline and one rule set for bosses, boss-fight members and regular enemies.
4. Identical results on every multiplayer machine, with far less network data than today.
5. No per-frame cost, and constant cost per hit. No allocations on transforms or projectile spawns.
6. BalanceLab keeps linking the scaling maths, and a guard test fails if new code reads raw enemy stats for combat.

## 3. Rulings

| # | Ruling |
|---|---|
| R1 | **Scale at read time.** Damage and defense are never written with scaled values. HP, coin value and size are written: at spawn, and again after every `SetDefaults` re-run. |
| R2 | **Boss-fight membership comes from the spawn record, not the `boss` flag.** An NPC whose spawner (NPC, or projectile → its NPC) belongs to a fight joins that fight. Otherwise a boss, or an NPC in `NPCID.Sets.ShouldBeCountedAsBoss`, starts a new fight. Everything else is a regular enemy. Parts and minions are not distinguished. Two bosses from one summon (the Twins) keep separate rolls. |
| R3 | **Fight members copy the whole boss package:** level, phase, the added fire/cold/lightning/chaos damage, resistances, penetration and sundering. They get no rarity and no modifiers. |
| R4 | **Enemy projectiles scale with their shooter** (restores behaviour lost in `9c1b087`). The base is vanilla's own projectile maths (the ×2 and the difficulty multiplier), times the shooter's damage transform. |
| R5 | **Vanilla's defense mechanic is fully replaced for every enemy.** Vanilla's defense step is zeroed, and its "negative defense → flat bonus damage" is cancelled. Defense acts only through the mod's resistance. |
| R6 | **Vanilla defense reducers fold into the mod's system.** Ichor, Betsy's Curse, and vanilla flat and percentage armor pen reduce effective defense points before conversion, alongside the mod's armor-pen affixes. |
| R7 | **Defense converts with a mirrored curve:** `sign(d) · cap · |d| / (|d| + halfPoint)`. Negative defense gives negative resistance with the same diminishing returns. |
| R8 | **No resistance floor anywhere, in either direction.** Enemy resistances can be pushed below 0 by player penetration, and player resistances by enemy penetration and sundering. `ApplyResistance` clamps only at the cap. |

## 4. Design

### 4.1 Enemy profile

`EnemyProfile` is an **immutable** object built once per combat NPC. It holds:

- **Inputs** (what is synced): level, phase, rarity, modifiers (type + magnitude), and the kind (Regular / FightMember) with the boss-package tier.
- **Derived, computed once at construction:**
  - the damage transform coefficients and defense transform coefficients (level multiplier with `Pow` done once, the additive defense floor, rarity %, modifier %);
  - the combat package: added fire/cold/lightning/chaos damage %, resistances, penetration, sundering.

  It is built by the existing pure `EnemyScaling` functions, so every machine derives identical values from identical inputs.

A single `GlobalNPC`, `EnemyProfileNPC`, holds the profile reference for every NPC it applies to (the union of the NPCs `NPCManager` and `BossManager` cover today). It also owns the little behaviour those two classes had left: Soul Drinker, the boss announcement, and the defense-step hook, which now covers every managed NPC. `NPCManager` and `BossManager` are deleted: once scaling, fields and sync move out, nothing remains in them.

Because the profile is immutable it is **shared by reference**:
- every member of one fight references the fight's profile;
- a projectile references its shooter's profile;
- a transform moves the reference.

### 4.2 Creation (server / single player, `OnSpawn`)

Creation follows R2:
1. **The spawn source is `EntitySource_Parent`**, and its entity is an NPC with a fight profile, or a projectile whose profile is a fight profile. The NPC references that profile.
2. **Else, if `npc.boss` or `NPCID.Sets.ShouldBeCountedAsBoss[type]`:** build a new fight profile. The level roll is the same as `BossManager`'s today (`[cap, cap×1.25]`), and the tier comes from `EnemyScaling.BossElementalTier()`.
3. **Else, a regular enemy:** build a new regular profile. The level roll is the same as `NPCManager`'s today (`[0.75·cap, 1.1·cap]`), along with the rarity and modifier rolls. The roll moves from `SetDefaults` to `OnSpawn`, so it happens once per spawn.

Clients never roll. Then the spawn-time writes (4.3) apply.

### 4.3 Spawn-time writes

The only vanilla fields the mod writes:
- **max life**, scaled through the level, rarity and Colossal/Tiny transform, with life set to the new max;
- **coin value**;
- **size** (Colossal/Tiny).

`npc.damage` and `npc.defense` are never written. The profile's presence is the "already scaled" marker, which replaces `statChanged`. Vanilla Expert/Master life and the mod's player-count life (`BossPlayerScaling`'s `ScaleStats` detour) run inside `SetDefaults`, before these writes, as today.

### 4.4 `SetDefaults` re-run hook

A detour on `NPC.SetDefaults(int, NPCSpawnParams)` (`On_NPC.SetDefaults`) covers every path that re-runs `SetDefaults` on a live NPC:
- `Transform` and the EoW segment conversion, both through `SetDefaultsKeepPlayerInteraction`;
- the natural spawner's immediate `SetDefaults(variant)` after `NewNPC` for slime colours and zombie variants (NPC.cs:88766-89251);
- the client's NPC packet (MessageBuffer.cs:1823).

`NewNPC` builds a fresh `NPC` object (NPC.cs:91614), so a brand-new spawn is always inactive when its `SetDefaults` runs, and nothing is carried. A negative netID re-enters `SetDefaults`; a depth counter makes only the outermost call act.

1. **Before:** if the NPC is active and has a profile, keep the profile reference and the health fraction `life / lifeMax`.
2. **Call the original.** Vanilla resets the stats and tModLoader re-creates the globals.
3. **After:** re-attach the same profile reference. Record the new vanilla max life, value and scale as the base for the writes. Re-apply the spawn-time writes from that base and set `life = round(lifeMax × fraction)`, at least 1.

A live NPC that becomes a managed type without a profile (a critter turning into an enemy) gets a fresh regular profile, on the server only.

`Transform`'s own `if (lifeMax == oldLifeMax) life = oldLife` then keeps the exact number whenever both forms scale to the same max life, as the spiders do. There is no re-roll and no heal.

On clients, the NPC packet re-creates the NPC with plain `SetDefaults` (MessageBuffer.cs:1823). The profile arrives in the same packet's mod data, and 4.9 re-applies the writes.

### 4.5 Accessor

`EnemyStats` in ES is the only way mod code reads combat numbers:

- `Profile(NPC)`: the profile, or null for unmanaged NPCs.
- `ContactDamage(NPC)`: `ScaleDamage(npc.damage, profile)`.
- `Defense(NPC)`: `ScaleDefense(npc.defense, profile)`.
- `ProjectileDamage(Projectile)`: see 4.7.

`ScaleDamage` and `ScaleDefense` are pure functions in `EnemyScaling`. They reproduce today's stage order and integer truncation for one stat:
- damage: level, then rarity, then Strong;
- defense: level (floor add + multiplier), then rarity, then Durable.

The result is identical to today's `ApplyLevelScaling` → `ApplyRarityStats` → `ApplyModifier` sequence. BalanceLab links them.

### 4.6 Player hits an enemy

1. **Defense step:** `ModifyIncomingHit` zeroes `modifiers.Defense` for every managed NPC and cancels the `FlatBonusDamage` vanilla adds for negative `npc.defense` (NPC.cs:92076-92083).
2. **Effective defense points** (`TargetResistances`), in vanilla's order:
   - start from `EnemyStats.Defense(npc)`;
   - apply percentage pen: vanilla `ScalingArmorPenetration` plus the mod's `PercentageArmorPen`;
   - subtract flat reductions: Ichor 15, Betsy's Curse 40, vanilla `ArmorPenetration`, and the mod's `FlatArmorPen`.

   **No floor.** Percentage pen applies only to positive defense, so it never lessens a vulnerability.

   Vanilla's reducers are computed from the same sources vanilla reads, not from the hit's modifiers, because vanilla adds some of them after the mod's hooks run (Player.cs:19361-19363 for direct strikes, Projectile.cs:12294 vs 12914):
   - Ichor 15 and Betsy's Curse 40 (NPC.cs:92084-92093);
   - flat pen: the projectile's `ArmorPenetration` (Projectile.cs:12293), `Player.GetWeaponArmorPenetration(item)` (Player.cs:44170), or, for direct strikes, `GetTotalArmorPenetration(class)` plus the repeated weapon's own pen (Player.cs:19363);
   - full scaling pen for the DD2 Lightning Aura projectiles against anything but the Dungeon Guardian (Projectile.cs:12908-12914), and for the Flymeal against town NPCs (Player.cs:44082-44171).
3. **Physical resistance** = `ConvertDefenseToResistance(effectiveDefense)` with the mirrored curve (R7).
4. **Elemental resistance** = profile resistance minus the player's penetration, with no floor.
5. Every portion goes through `ApplyResistance`, which clamps at the cap only (R8).

### 4.7 Enemy hits the player (`PlayerHurtPipeline`)

- **Contact:** base = `Main.DamageVar(EnemyStats.ContactDamage(npc))`. `npc.damage` already includes vanilla's Expert/Master scaling.
- **Projectile:** base = vanilla's enemy-projectile damage maths, times `ScaleDamage` from the projectile's profile. Vanilla's maths are:
  - `Main.DamageVar(damage) × 2` (Projectile.cs:13804);
  - `EnemyDamageMultiplier`, or Journey's strength slider (CombinedHooks.cs:237-246), with vanilla's exceptions: reflected projectiles and `ProjectileID.Sets.PlayerHurtDamageIgnoresDifficultyScaling` skip it (CombinedHooks.cs:233-236), and the 0.7× case just above it (CombinedHooks.cs:229-232) is kept. The plan reads the full `CombinedHooks.ModifyHitByProjectile` and ports it whole. A projectile without a profile (traps, boulders) gets vanilla maths only. The Strong modifier lives in the transform, so `projectile.damage` is never written.
- **Elemental portions, penetration and sundering** come from the source's profile.
- **Player resistances:** the 0 floors on effective defense and elemental resistance are removed (R8). Player physical resistance uses the same mirrored conversion.

### 4.8 Projectile profile

In `ProjectileManager.OnSpawn`, a projectile whose source is `EntitySource_Parent` references:
- the NPC's profile, when the parent is an NPC;
- the parent projectile's profile, for split and chained shots.

The reference survives the shooter's death. The profile's inputs are synced with the projectile (4.9).

### 4.9 Multiplayer

**NPC packet mod data** replaces today's 62–78 bytes (`NPCManager`) or ~63 bytes (`BossManager`):

| Field | Encoding |
|---|---|
| has-profile, server-life-is-full | 2 bits (`BitWriter`) |
| level | 7-bit int (1–2 B) |
| kind + tier/phase + rarity | 1 byte packed |
| modifier count | 1 byte |
| per modifier | type byte + 7-bit magnitude (2–3 B) |

The total is about 3–9 bytes. Damage, defense, max life and the 13 elemental floats are derived, not sent.

`ReceiveExtraAI`:
- **If the inputs equal the current profile's inputs:** nothing happens. This is the common case: every movement packet costs a comparison and no allocation.
- **Otherwise** it builds the profile and, on a fresh instance, applies the max-life, value and size writes. It never overwrites `life` with a computed value.
- **If "server-life-is-full" is set,** `life = lifeMax` after scaling. This is needed because vanilla's full-life flag sets `life` to the client's pre-scaling `lifeMax` before mod data is read (MessageBuffer.cs:1843-1845).

**Projectile packet mod data:**
- a has-profile bit;
- for enemy projectiles, the same compact profile plus the shooter's slot (7-bit int). The slot is used only by kaeshi's counter-strike, never for scaling.

Today every projectile carries the slot, player projectiles included, and scaling is looked up through it, which breaks when the shooter's slot is reused. A client builds the profile only when the inputs change.

**Config:** the scaling constants come from `ARPGEnemySystem.Common.Configs.Config`, which is `ConfigScope.ServerSide`, so tModLoader syncs it. Every machine derives the same numbers.

### 4.10 Other readers

- **`XPAwardGlobalNPC`:** `npc.lifeMax / 5 + EnemyStats.ContactDamage × 2 + EnemyStats.Defense × 2`.
- **`NPCTooltip`:** one builder from the profile and the accessor. The Phys Res line uses the same conversion as combat and can be negative. The header shows level, rarity and modifiers for a regular enemy, and level for a fight member.
- **`Mod.Call("GetEnemyInfo")`** keeps its shape: `[level, rarityTier]`, with tier 0 for fight members, and null for unmanaged NPCs.

## 5. Performance

| Where | Today | After |
|---|---|---|
| Per NPC per frame | `NPCManager.PreAI` dispatched for every managed NPC | none (the PreAI override is removed) |
| Per hit | two `TryGetGlobalNPC` attempts, field-by-field copy | one lookup, a few multiply/adds on precomputed coefficients |
| Transform | re-roll plus re-scale | one reference move plus the life/value/size writes |
| Projectile spawn | slot index stored | one reference stored; no allocation |
| Tooltip | per hovered NPC | same, with a few multiply/adds |
| NPC packet | 62–78 B mod data | 3–9 B |

## 6. Verification

- **Guard test (BalanceLab):** scans ES, CS and IS sources for reads of an NPC's `damage`/`defense`. Allowed readers are listed in the test: the `EnemyStats` accessor, the spawn and re-run hooks, and the defense-step hook.
- **Pure-maths tests (BalanceLab, on linked code):**
  - `ScaleDamage`/`ScaleDefense` match today's staged sequence;
  - scaling the same raw value repeatedly gives the same result, with no compounding;
  - the mirrored conversion is symmetric, continuous at 0, and has no pole;
  - `ApplyResistance` has no floor;
  - projectile damage includes the ×2 and the difficulty multiplier;
  - the profile encoding round-trips.
- **BalanceLab re-sync:**
  - remove the `AiResetsDamageDefense` special case and the boss `defense/2` subtraction;
  - projectile biggest hit = vanilla projectile maths × level transform, which supersedes the half-done S11 divisor;
  - re-mirror the files the drift guard flags and re-fingerprint them;
  - run **eval round 4** and compare it with round 3.
- **In-game checklist:**
  1. A wall spider switching form keeps its HP and level. It never heals.
  2. A split EoW segment keeps the fight's level and its health fraction.
  3. Plantera's tooltip damage and defense are scaled and change with her phase and enrage.
  4. Skeletron's hands show the head's level with no rarity. Servants show EoC's level.
  5. Enemy projectile damage grows with the shooter's level. Traps don't scale.
  6. Ichor and armor pen lower phys res on the tooltip, below 0 included.
  7. In multiplayer, a client takes scaled contact and projectile damage, and a joining client sees correct max life and full-HP bars.
  8. FancyHealthbar shows level and rarity.

## 7. Documentation

- **ES `CLAUDE.md`:** rewrite the Architecture data flow, including the false line "no level-based damage scaling here — NPC stat scaling already carries through".
- **CS `docs/systems`:** update the combat and hurt-pipeline pages (armor pen, negative resistance, projectile scaling).
- **BalanceLab `docs/ASSUMPTIONS.md`:** remove the approximations this fixes.

## 8. Out of scope

- **Vanilla damage reduction erased on player hits** (Endurance, Beetle, Solar, Paladin). This is a follow-up fix in `PlayerHurtPipeline`.
- **The balance retune.** It is driven by eval round 4.
- **ES `Rarity.cs` weight column summing to 95.**
