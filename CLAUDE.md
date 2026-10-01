# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Development

This is a tModLoader mod for Terraria targeting .NET 8. To build:

- **In-game (recommended):** tModLoader → Workshop → Mod Sources → select `ARPGEnemySystem` → Build & Reload
- **CLI compile check:** `dotnet build` (verifies compilation but does not deploy)

There are no automated tests. Testing requires running tModLoader with the mod loaded.

## In-game Debug Commands

- `/checklevelcap` — reports the world level, `downed/N` from the boss roster, the per-boss value, and which entries are downed
- `/setlevelcap <value>` — holds `WorldManager.levelCap` for testing; `/setlevelcap auto` releases it back to boss progression

## Cross-Mod Dependency

ARPGEnemySystem and ARPGItemSystem are **mutually required** — neither will load without the other.

- `ARPGItemSystem.build.txt` declares `modReferences = ARPGEnemySystem` (sets load order: EnemySystem loads first).
- The reverse direction cannot use `modReferences` without creating a load-order cycle. Instead, `ARPGEnemySystem.cs` does a runtime `HasMod("ARPGItemSystem")` check in `PostSetupContent` and throws an `Exception` if absent. `PostSetupContent` runs after every mod's `Load()` so the check sees the final loaded set.
- Because the requirement is enforced, elemental-system code is **no longer gated** on `HasMod("ARPGItemSystem")` — it always runs. Don't reintroduce those guards.

**Boss Checklist** is a hard requirement declared the normal way — `modReferences = BossChecklist` in `build.txt`. No load-order cycle exists, so no runtime `HasMod` check is needed for load ordering — but `BossRoster` still throws if the mod is missing (`Common/Systems/BossRoster.cs:27-28`), as a defensive guard against a force-disabled dependency. It is the sole source of the boss roster that drives world level: `BossRoster` calls `GetBossInfoDictionary` (API version `1.6`) in `PostAddRecipes` and keeps each `isBoss` entry's `downed` predicate. No `.csproj` `Reference` is needed because the integration is `Mod.Call` returning `object`.

**`Mod.Call` API (added 2026-09-21):** `Call("GetEnemyInfo", NPC npc)` → `int[] { level, rarityTier }` (`rarityTier` is `(int)Rarity`, 0 = None; a fight member reports its fight's level with tier 0) or `null` for an NPC `EnemyProfileNPC` doesn't apply to; a returned `level` of 0 means the NPC is managed but its profile has not yet synced to this client, so the caller should keep polling until it is non-zero rather than displaying it. Read-only, consumed by the standalone Fancy Healthbar mod for its rarity frame and level badge; keep the shape stable or version the name.

## Architecture

### High-Level Concept

Every combat NPC gets one immutable **EnemyProfile** (`Common/Scaling/EnemyProfile.cs`) at spawn, from `EnemyProfileNPC.OnSpawn` on the server:

- **Boss fights.** A boss, or an NPC in `NPCID.Sets.ShouldBeCountedAsBoss`, starts a fight: one level roll plus the boss elemental package. Anything a fight member spawns joins the fight, directly or through a projectile it fired, and shares the same profile: parts, segments and minions alike. They get no rarity and no modifiers.
- **Everything else** is a regular enemy: a level, an **EnemyRarity** and 0–8 **EnemyModifiers** (by rarity and phase).
- **Level cap:** runs from 10 on a fresh world to 200 when every boss in the loaded mod set is dead. It is derived at runtime, not persisted.
- **Damage and defense are never written.** Every reader goes through `EnemyStats` (`Common/GlobalNPCs/EnemyStats.cs`), which scales whatever vanilla holds at that moment. Vanilla AI rewrites them mid-fight (Plantera, Skeletron, Duke, Empress every frame).
- **Only max life, coin value and size are written.** This happens at spawn and after every `NPC.SetDefaults` re-run on a live NPC on the server: Transform, EoW splits, natural-spawn variants. There the `On_NPC.SetDefaults` detour carries the profile and the health fraction. On clients the detour does nothing; the profile arrives in extra-AI and the same writes are applied from it.
- **Enemy projectiles** reference their shooter's profile and scale with it.
- **Vanilla's defense step is zeroed for every enemy.** Defense acts only through the mod's physical resistance, which converts with a mirrored curve and has no floor.

The vanilla prefix/reforge system is not touched by this mod; it affects NPCs, not items.

`EnemyProfileNPC.AppliesToEntity`: `boss`, or not `townNPC` / `friendly` / `CountsAsACritter` / `TargetDummy`. The `friendly` guard is required in addition to `townNPC` because some NPCs (Skeleton Merchant, Old Man) are friendly but not flagged as town NPCs.

### Core Data Flow

```
BossRoster (Boss Checklist roster + downed predicates)
       ↓ (polled once per second, server only)
WorldManager.levelCap = 10 + (190 / N) × downed
       ↓
EnemyProfileNPC.OnSpawn (server)  → fight profile (join or start) or regular profile (level + rarity + modifiers)
       ↓                            writes max life / value / size once
On_NPC.SetDefaults detour         → server only: re-attaches the same profile after any SetDefaults re-run, keeps the health fraction
SendExtraAI / ReceiveExtraAI      → 3–20 B (+2 bits) of profile inputs; clients rebuild, never roll
       ↓ (read at hit / tooltip / XP time)
EnemyStats.ContactDamage / Defense / ProjectileDamage → profile applied to vanilla's current raw value
EnemyProfileNPC.ModifyIncomingHit → zeroes vanilla defense (resistance replaces it)
ProjectileManager.OnSpawn         → projectile references its shooter's (or parent projectile's) profile
```

### Scaling Formulas

HP and damage use a shared multiplier:

```
multiplier = 1 + level^ScalingExponent × PhaseRates[phase]
```

Defense uses a **separate steeper curve** plus an **additive level floor**:

```
defense += (int)(level × DefenseFloor)          // floor: lifts all enemies, preserves relative gaps
defense  = (int)(defense × defMultiplier)       // defMultiplier = 1 + level^DefScalingExponent × DefPhaseRates[phase]
```

`defense` here is vanilla's current `npc.defense`, scaled at every read by `EnemyStats.Defense`; it is never written back. Damage works the same way.

The additive floor ensures low-defense enemies (slimes: 2 defense, zombies: 6 defense) have meaningful physical resistance at all progression stages. Without it, the hyperbolic conversion (`cap × |defense| / (|defense| + halfPoint)`, mirrored below zero) would yield near-zero physRes for weak enemies while elemental resistances (level-based) would be 25–75%, making physical damage trivially effective against them. The steeper defense curve means armor penetration affixes become increasingly load-bearing at high levels.

**Phase rates and level bounds are constants, not config** (game design values, on `ScalingMath`; `WorldManager` aliases them). The exponents, defense floor and half-point below are server config.

Constants on `ScalingMath` (phase-indexed arrays):

| Constant        | Value                          | Purpose                             |
| --------------- | ------------------------------ | ----------------------------------- |
| `PhaseRates`    | `{0.003, 0.006, 0.010, 0.015}` | HP/damage scaling per phase         |
| `DefPhaseRates` | `{0.004, 0.008, 0.013, 0.020}` | Defense scaling per phase (steeper) |
| `BaseLevel`     | `10`                           | World level on a fresh world        |
| `MaxLevel`      | `200`                          | World level with every boss downed  |

Server config knobs (tunable per-server):

| Config field         | Default | Purpose                                            |
| -------------------- | ------- | -------------------------------------------------- |
| `ScalingExponent`    | `1.14`  | HP/damage curve shape                              |
| `DefScalingExponent` | `1.15`  | Defense curve shape (steeper than ScalingExponent) |
| `DefenseFloor`       | `0.70`  | Additive min defense = level × floor               |
| `PhysResHalfPoint`   | `60`    | Defense at which physRes reaches half the cap      |

**Reference values** (HP multiplier at key milestones with defaults):

- Level 50, phase 0 (pre-HM): 1.26× (+26%)
- Level 50, phase 1 (post-WoF): 1.52× (+52%)
- Level 100, phase 2 (post-mechs): 2.91× (+191%)
- Level 150, phase 3 (post-Plantera): 5.54× (+454%)
- Level 200, phase 3 (everything downed): 7.30× (+630%)

### Phase System

`WorldManager.GetScalingPhase()` returns 0–3:

- Phase 0 — pre-hardmode
- Phase 1 — post-WoF (`Main.hardMode`)
- Phase 2 — post-all-three-mechs (`NPC.downedMechBoss1 && downedMechBoss2 && downedMechBoss3`)
- Phase 3 — post-Plantera (`NPC.downedPlantBoss`)

Phase changes produce discrete difficulty jumps: the same level enemy becomes significantly harder after each milestone. PhaseRates are the "bump" — same level, different rate, higher multiplier.

`GetScalingPhase()` tops out at 3, so every post-Moon-Lord modded boss fight uses `PhaseRates[3]`. This is a known limitation, deliberately left out of the world-level work.

### Key Files

- **`Common/Systems/BossRoster.cs`** — Pulls every `isBoss` entry from Boss Checklist in `PostAddRecipes` and keeps its `downed` predicate. `IsDowned(entry)` is the only place a third-party delegate is invoked; an entry whose predicate throws is logged and excluded for the session. `progression` is available on each entry but deliberately unused. It also collects the `npcIDs` of every `isBoss` and `isMiniboss` entry into the set `BossPlayerScaling` reads; Boss Checklist does not export its limb lists (`npcLimbs` is internal), so limbs are not in it. Eater of Worlds and Brain of Cthulhu are two roster entries sharing one `downed` flag, so killing either is counted twice — the Eater of Worlds is worth two bosses' worth of level cap on a corruption world.
- **`Common/Systems/BossPlayerScaling.cs`** — Detours `NPC.ScaleStats` so every NPC type in a Boss Checklist boss or miniboss entry's `npcIDs` (`BossRoster.ScalesWithPlayers`) gets **solo life × player count** in Expert/Master, replacing vanilla's `balance` curve (1.35× at 2 players, 2.63× at 4). It runs the original with a player count of 1, so vanilla's and the boss mod's own `ApplyDifficultyAndPlayerScaling` produce solo values, then multiplies `lifeMax`. Deliberately unchanged: Normal mode (vanilla has no player scaling there; detected because `statsAreScaledForThisManyPlayers` is only set by vanilla's Expert branch), limbs not listed in `npcIDs` (Skeletron hands, Prime limbs, Golem head/fists, Moon Lord hands, Creepers keep vanilla's partial scaling), mid-fight joins (count frozen at spawn, as vanilla), and damage. `statsAreScaledForThisManyPlayers` is restored to the real count afterwards, because Golem's AI (`GetMyBalance`) and the NPC spawn packet read it, and clients rebuild `lifeMax` from that synced count through the same detour. A mod that adds its own player scaling outside `ApplyDifficultyAndPlayerScaling` would stack with this; that can only be caught in-game. `Announce` (called from `EnemyProfileNPC.OnSpawn` once the spawn writes are applied, server/SP only) broadcasts one chat line per boss type when the server config's `EnableBossScalingLog` is on, at most once per 60 ticks so a worm boss's segments do not flood chat: solo life, life after player scaling, final life and level. The toggle is server-side, unlike the client-side debug logs, because the server decides and broadcasts the line.
- **`Common/Systems/WorldManager.cs`** — Owns `levelCap` and `GetScalingPhase()`; it only aliases `PhaseRates`/`DefPhaseRates`/`BaseLevel`/`MaxLevel`, which are declared on `ScalingMath` (`Common/Scaling/ScalingMath.cs`). `Recompute()` sets `levelCap = BaseLevel + LevelsPerBoss() × BossRoster.DownedCount()`, called from `PostWorldLoad` and every 60 ticks in `PostUpdateWorld` — both server/single-player only, so the server is authoritative without a `netMode` check. The result is rounded to the nearest int (`MathF.Round`), and a roster of zero bosses pins `LevelsPerBoss()` at 0, so `levelCap` stays at `BaseLevel`. Nothing is persisted: `levelCap` is a pure function of the roster and the downed predicates. `levelCapOverride` (−1 = off) lets `/setlevelcap` hold a value against the recompute.
- **`Common/GlobalNPCs/EnemyProfileNPC.cs`** — `GlobalNPC` that owns every combat NPC's `EnemyProfile` (`Profile` field). `OnSpawn` (server/SP) creates or joins a profile and writes max life, coin value and size; the `On_NPC.SetDefaults` detour (server/SP only, outermost call only) re-attaches the profile and health fraction after any re-run; `ModifyIncomingHit` zeroes vanilla defense (and cancels vanilla's negative-defense bonus). ARPGItemSystem is a hard mutual requirement, so elemental packages always apply — see Cross-Mod Dependency.
- **`Common/Scaling/EnemyProfile.cs`** — The immutable profile: `Kind` (`Regular` / `FightMember`), `Level`, `Phase`, `Rarity`, `Modifiers`, and `Package` (elemental damage %, resistances, penetration, sundering). `ScaleDamage` / `ScaleDefense` / `ScaleLifeMax` apply level, then rarity, then modifiers to a raw value. A fight member's package is progression-tiered by `EnemyScaling.BossTierForPhase`: resistances 25/50/75% (chaos 6/13/19%), damage % 10/12/14 (chaos 4/5/6), elemental penetration 5/10/15 (chaos 3/5/8), Sundering 15/30/45 from its own array. `PhysicalResistance` is NOT stored — derived at hit time from `EnemyStats.Defense` via `ConvertDefenseToResistance`.
- **`Common/GlobalNPCs/EnemyStats.cs`** — The only way mod code reads an enemy's or enemy projectile's damage and defense. Also owns the Ichor (−15) and Betsy's Curse (−40) defense reductions that vanilla's zeroed defense step no longer applies.
- **`Common/Scaling/EnemyProfileCodec.cs`** — Wire format for the profile inputs (level, kind/phase/rarity header byte, modifier count, type + magnitude per modifier), shared by NPC and projectile sync.
- **`Common/GlobalNPCs/Rarity.cs`** — `EnemyRarity` struct + `RarityDatabase`. Five rarities (Common / Uncommon / Rare / Elite / Legend). Roll weights (in `rarityWeightDatabase`) shift toward higher rarities across 8 columns, each tied to a boss milestone in `GetWeightIndex()`. Stat bonuses: Common 0/0/0, Uncommon 20/10/10, Rare 50/25/20, Elite 100/50/35, Legend 200/100/60 (HP%/Def%/Dmg%).
- **`Common/GlobalNPCs/EnemyModifier.cs`** — `EnemyModifier` struct + `ModifierType` enum. An excludeList passed to `GenerateModifier` prevents duplicate modifier types on the same enemy.
- **`Common/Database/TierDatabase.cs`** — Static dictionary: `ModifierType → List<Tier>(10 entries)`. Tier 0 = highest values, tier 9 = lowest. `Utils.GetTier()` returns an index based on boss progression (minimum and maximum tier both shrink as more bosses die).
- **`Common/UI/UISystem.cs`** + **`Common/UI/NPCTooltip.cs`** — `UISystem` (ModSystem) hooks into `ModifyInterfaceLayers` to draw the overlay. `NPCUI` (UIState) rebuilds a `UITextPanel` on every update tick. Now shows: level, rarity, modifiers, defense, Phys Res (computed via `ConvertDefenseToResistance(EnemyStats.Defense(npc) − Ichor/Betsy reduction, PhysResHalfPoint, ElementalMath.ElementCap)`), Fire/Cold/Lightning Res, and elemental damage type/pct. Controlled by `ConfigClient.EnableEnemyStatPanel`.
- **`Common/Elements/Element.cs`** — `Element` enum (`Physical=0`, `Fire=1`, `Cold=2`, `Lightning=3`), byte-backed for efficient serialization.
- **`Common/Elements/ElementalMath.cs`** — Static helpers: `ClampResistance(raw, cap)`, `ApplyResistance(damage, res%, cap)`, `ConvertDefenseToResistance(defense, halfPoint, cap)` = `sign(d) · cap · |d| / (|d| + halfPoint)`. A hyperbolic curve mirrored below zero: `halfPoint` is the defense value at which physRes reaches `cap / 2`, and negative defense is a vulnerability. No floor. This is how an enemy's scaled defense becomes a physical resistance percentage.
- **`Common/Configs/Config.cs`** — Server-side: `ScalingExponent` (default 1.14, HP/damage curve shape), `DefScalingExponent` (1.15), `DefenseFloor` (0.7), `PhysResHalfPoint` (int, default 60, defense value at which physRes reaches cap/2), and the `EnableBossScalingLog` debug toggle (default false). The resistance caps are not config: `ElementalMath.ElementCap` (75) and `ElementalMath.PlayerPhysCap` (80). Client-side (`ConfigClient`): `EnableEnemyStatPanel` (default false), `EnableElementalDamageLog` (default false, debug toggle for chat hit log), `EnableReapLog` (default false, debug toggle for the reap log). Enemy modifiers always roll — the previous `ModifierAllowed` toggle was removed.

### Multiplayer Sync

`EnemyProfileNPC.SendExtraAI/ReceiveExtraAI` syncs the profile's inputs (`EnemyProfileCodec`: level, kind/phase/rarity, modifier types and magnitudes; 3–20 bytes) plus a has-profile bit and a full-life bit. Clients rebuild the profile from those inputs and the server-synced config, then apply the same max life / value / size writes; they never roll. The full-life bit restores `life` after the rebuild, because vanilla sets it from the pre-scaling `lifeMax` before extra-AI is read. `ProjectileManager.SendExtraAI/ReceiveExtraAI` syncs the shooter's profile the same way, plus the shooter's NPC slot, which names the source in the hurt log and picks the kaeshi counter-strike's attacker.

`WorldManager.NetSend`/`NetReceive` send `levelCap` as one int to joining clients, and `SendLevelCap()` broadcasts an `EnemyPacketType.LevelCap` packet whenever the recompute changes it. Clients never compute `levelCap` — `EnemyProfileNPC.OnSpawn` and the `SetDefaults` detour both do nothing on a multiplayer client, and NPC profiles arrive through `SendExtraAI`.

### Adding a New Modifier

1. Add an entry to `ModifierType` enum in `Common/GlobalNPCs/EnemyModifier.cs`
2. Add 10 `Tier` entries to `TierDatabase.modifierTierDatabase` in `Common/Database/TierDatabase.cs`
3. Add the stat effect as a `case` in `EnemyScaling.ApplyModifier`. It only edits the `EnemyStatBlock`; the profile applies it at read time (damage, defense) or at the spawn writes (max life, size). There is no per-tick hook — per-tick velocity manipulation was removed because it compounds uncontrollably for accumulation-based NPC AI.
4. Add an `OnHitPlayer` case in both `EnemyProfileNPC` and `ProjectileManager` for debuffs applied on hit

**Removing a modifier:** Delete its enum value from `ModifierType`, remove its entry from `TierDatabase.modifierTierDatabase`, and remove any switch cases in `EnemyScaling.ApplyModifier` and `OnHitPlayer`. NPCs don't persist to disk and both sides of a multiplayer session always share the same mod version, so there is no save-migration or network-sync concern from renumbering the enum.

### Adding a New Rarity

Add a row to both `RarityDatabase.rarityModifierDatabase` (3-element list: HP%, defense%, damage% magnitudes) and `rarityWeightDatabase` (8-element weight list matching the 8 boss milestone columns in `GetWeightIndex()`). Weights across all rarities must sum to 100 per column.

## NPC Fields — Scaling & Power Level

Key NPC fields relevant to enemy scaling. Read damage and defense through `EnemyStats`, never off the field directly: vanilla AI rewrites them mid-fight and the mod never writes them.

| Field          | Type  | Purpose                        | Notes                                                                                                |
| -------------- | ----- | ------------------------------ | ---------------------------------------------------------------------------------------------------- |
| `npc.lifeMax`  | int   | Max health                     | Use this, not `npc.life`, for baseline                                                               |
| `npc.damage`   | int   | Contact/projectile damage stat | Vanilla's current value; scaled at read by `EnemyStats.ContactDamage`, never written                |
| `npc.defense`  | int   | Vanilla defense                | Vanilla's step zeroed by `EnemyProfileNPC.ModifyIncomingHit`; scaled by `EnemyStats.Defense`, then converted to physical resistance |
| `npc.npcSlots` | float | Spawn weight contribution      | Bosses ≈ 6f, mini-bosses ≈ 2–3f, normal enemies = 1f, critters = 0.1–0.25f                           |
| `npc.value`    | float | Coin drop value (in copper)    | Rough economy proxy; set by vanilla per enemy type                                                   |

### Negative netID NPCs

Many vanilla NPCs have negative netIDs (variant NPCs — e.g. slime variants −10 to −5, zombie variants −55 to −26). `npc.netID` is assigned **after** `SetDefaults` completes, and variant-specific stats are finalized after that. Consequences:

- **`SetDefaults`**: cannot check `npc.netID` (not yet assigned); variant-specific stats may be overwritten after this hook returns.
- **Variant stats are re-set after `OnSpawn`.** The natural spawner runs a variant `SetDefaults` right after `NewNPC`, which would drop the max-life write. The `On_NPC.SetDefaults` detour re-applies the profile and health fraction after that re-run, so the profile survives it. Damage and defense need no such handling, since they are never written.

**`npc.rarity` is NOT a power-level indicator.** It is the Lifeform Analyzer detection priority (values 0–4), used only to decide which creature to display when multiple rare enemies are nearby. Do not use it for difficulty or coefficient calculations. Modders do not reliably set it.

### Cross-Mod Integration Points

- **`Common/Utils.cs`** — `GetXPMultiplier(rarity, level, modifierCount)`: integration point for `ARPGCharacterSystem`'s XP math. Initially returns the same value as `GetCoinMultiplier`; symbols are separate so coin and XP yields can diverge.
