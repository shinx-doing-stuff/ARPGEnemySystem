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

**`Mod.Call` API (added 2026-09-21):** `Call("GetEnemyInfo", NPC npc)` → `int[] { level, rarityTier }` (`rarityTier` is `(int)Rarity`, 0 = None; a boss reports its `BossManager` level with tier 0) or `null` for an NPC neither manager applies to; a returned `level` of 0 means the NPC is managed but its level has not yet synced to this client, so the caller should keep polling until it is non-zero rather than displaying it. Read-only, consumed by the standalone Fancy Healthbar mod for its rarity frame and level badge; keep the shape stable or version the name.

## Architecture

### High-Level Concept

Every non-boss, non-critter, non-friendly enemy NPC is assigned a **level** and **EnemyRarity** on spawn, plus 0–2 random **EnemyModifiers**. Bosses receive a level only. The level cap runs from 10 on a fresh world to 200 when every boss in the loaded mod set is dead, and is derived at runtime rather than persisted. The vanilla prefix/reforge system is not touched by this mod; it affects NPCs, not items.

`NPCManager.AppliesToEntity` excludes: `townNPC`, `friendly`, `CountsAsACritter`, `boss`, and `TargetDummy`. The `friendly` guard is required in addition to `townNPC` because some NPCs (Skeleton Merchant, Old Man) are friendly but not flagged as town NPCs.

### Core Data Flow

```
BossRoster (Boss Checklist roster + downed predicates)
       ↓ (polled once per second, server only)
WorldManager.levelCap = 10 + (190 / N) × downed
       ↓ (consumed by)
NPCManager.SetDefaults()  → rolls level + modifiers + elemental properties
BossManager.OnSpawn()     → rolls level + elemental properties (progression-tiered)
       ↓ (applied in)
NPCManager.PreAI()        → scales base stats once (statChanged flag prevents re-entry)
BossManager.OnSpawn()     → scales stats immediately on spawn
NPCManager.ModifyIncomingHit() → zeroes vanilla NPC defense
       ↓ (propagated to)
ProjectileManager.OnSpawn() → applies Strong modifier bonus to NPC-sourced projectile damage
                              (no level-based damage scaling here — NPC stat scaling already carries through)
```

### Scaling Formulas

HP and damage use a shared multiplier:

```
multiplier = 1 + level^ScalingExponent × PhaseRates[phase]
```

Defense uses a **separate steeper curve** plus an **additive level floor**:

```
npc.defense += (int)(level × DefenseFloor)          // floor: lifts all enemies, preserves relative gaps
npc.defense  = (int)(npc.defense × defMultiplier)   // defMultiplier = 1 + level^DefScalingExponent × DefPhaseRates[phase]
```

The additive floor ensures low-defense enemies (slimes: 2 defense, zombies: 6 defense) have meaningful physical resistance at all progression stages. Without it, the hyperbolic conversion (`cap × defense / (defense + halfPoint)`) would yield near-zero physRes for weak enemies while elemental resistances (level-based) would be 25–75%, making physical damage trivially effective against them. The steeper defense curve means armor penetration affixes become increasingly load-bearing at high levels.

**All scaling constants are hardcoded in `WorldManager` — not in config.** These are game design values.

Hardcoded in `WorldManager` (game design values, phase-indexed arrays):

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

- **`Common/Systems/BossRoster.cs`** — Pulls every `isBoss` entry from Boss Checklist in `PostAddRecipes` and keeps its `downed` predicate. `IsDowned(entry)` is the only place a third-party delegate is invoked; an entry whose predicate throws is logged and excluded for the session. `progression` is available on each entry but deliberately unused. Eater of Worlds and Brain of Cthulhu are two roster entries sharing one `downed` flag, so killing either is counted twice — the Eater of Worlds is worth two bosses' worth of level cap on a corruption world.
- **`Common/Systems/WorldManager.cs`** — Owns `levelCap`, the hardcoded `PhaseRates`/`DefPhaseRates`/`BaseLevel`/`MaxLevel`, and `GetScalingPhase()`. `Recompute()` sets `levelCap = BaseLevel + LevelsPerBoss() × BossRoster.DownedCount()`, called from `PostWorldLoad` and every 60 ticks in `PostUpdateWorld` — both server/single-player only, so the server is authoritative without a `netMode` check. The result is rounded to the nearest int (`MathF.Round`), and a roster of zero bosses pins `LevelsPerBoss()` at 0, so `levelCap` stays at `BaseLevel`. Nothing is persisted: `levelCap` is a pure function of the roster and the downed predicates. `levelCapOverride` (−1 = off) lets `/setlevelcap` hold a value against the recompute.
- **`Common/GlobalNPCs/NPCManager.cs`** — `GlobalNPC` for all regular enemies. Stores `level`, `rarity`, `modifierList`, plus elemental fields: `ElementalDamageType`, `ElementalDamagePct`, `FireResistance`, `ColdResistance`, `LightningResistance`. Elemental rolling and `ModifyIncomingHit` defense-zeroing always run (ARPGItemSystem is a hard mutual requirement — see Cross-Mod Dependency). Sync appends 5 elemental values after existing fields: `(byte)ElementalDamageType`, then 4 floats — read order must match write order exactly.
- **`Common/GlobalNPCs/BossManager.cs`** — `GlobalNPC` for bosses only. Level + stat scaling applied in `OnSpawn` (safe for bosses — no negative netID variants). No longer registers kills — world level polls downed state instead. Elemental properties are progression-tiered: pre-WoF=25%, post-WoF=50%, post-Plantera=75% (all elemental resistances + damage %). `PhysicalResistance` is NOT stored — derived at hit time via `ConvertDefenseToResistance(npc.defense, PhysResHalfPoint, ElementalResistanceCap)`.
- **`Common/GlobalNPCs/Rarity.cs`** — `EnemyRarity` struct + `RarityDatabase`. Five rarities (Common / Uncommon / Rare / Elite / Legend). Roll weights (in `rarityWeightDatabase`) shift toward higher rarities across 8 columns, each tied to a boss milestone in `GetWeightIndex()`. Stat bonuses: Common 0/0/0, Uncommon 20/10/10, Rare 50/25/20, Elite 100/50/35, Legend 200/100/60 (HP%/Def%/Dmg%).
- **`Common/GlobalNPCs/EnemyModifier.cs`** — `EnemyModifier` struct + `ModifierType` enum. An excludeList passed to `GenerateModifier` prevents duplicate modifier types on the same enemy.
- **`Common/Database/TierDatabase.cs`** — Static dictionary: `ModifierType → List<Tier>(10 entries)`. Tier 0 = highest values, tier 9 = lowest. `Utils.GetTier()` returns an index based on boss progression (minimum and maximum tier both shrink as more bosses die).
- **`Common/UI/UISystem.cs`** + **`Common/UI/NPCTooltip.cs`** — `UISystem` (ModSystem) hooks into `ModifyInterfaceLayers` to draw the overlay. `NPCUI` (UIState) rebuilds a `UITextPanel` on every update tick. Now shows: level, rarity, modifiers, defense, Phys Res (computed via `ConvertDefenseToResistance(npc.defense, PhysResHalfPoint, ElementalResistanceCap)`), Fire/Cold/Lightning Res, and elemental damage type/pct. Controlled by `ConfigClient.EnableEnemyStatPanel`.
- **`Common/Elements/Element.cs`** — `Element` enum (`Physical=0`, `Fire=1`, `Cold=2`, `Lightning=3`), byte-backed for efficient serialization.
- **`Common/Elements/ElementalMath.cs`** — Static helpers: `ClampResistance(raw, cap)`, `ApplyResistance(damage, res%, cap)`, `ConvertDefenseToResistance(defense, halfPoint, cap)` = `cap × defense / (defense + halfPoint)`. A hyperbolic curve: `halfPoint` is the defense value at which physRes reaches `cap / 2`. The conversion formula is how vanilla `npc.defense` becomes a physical resistance percentage.
- **`Common/Configs/Config.cs`** — Server-side: `ScalingExponent` (default 1.14, controls HP/damage curve shape), plus elemental entries: `ElementalResistanceCap` (default 75), `EnemyElementalChance` (default 67%), `EnemyBaseElementalAllocationPct` (default 25%), `PhysResHalfPoint` (int, default 30, defense value at which physRes reaches cap/2), `PlayerPhysResCap` (int, default 80), `EnemyElemResPerLevel` (default 0.005). Client-side (`ConfigClient`): `EnableEnemyStatPanel` (default false), `EnableElementalDamageLog` (default false, debug toggle for chat hit log). Enemy modifiers always roll — the previous `ModifierAllowed` toggle was removed.

### Multiplayer Sync

`NPCManager.SendExtraAI/ReceiveExtraAI` syncs level, rarity (as int), and the modifier list as two parallel int arrays (IDs and magnitudes). `BossManager.SendExtraAI/ReceiveExtraAI` syncs level plus the already-modified stat values directly. `ProjectileManager.SendExtraAI/ReceiveExtraAI` syncs the source NPC's `whoAmI` index so the client can look up the NPC's `NPCManager`/`BossManager`.

`WorldManager.NetSend`/`NetReceive` send `levelCap` as one int to joining clients, and `SendLevelCap()` broadcasts an `EnemyPacketType.LevelCap` packet whenever the recompute changes it. Clients never compute `levelCap` — `NPCManager.SetDefaults` and `BossManager.OnSpawn` are both guarded by `Main.netMode != NetmodeID.MultiplayerClient` and NPC levels arrive through `SendExtraAI`.

### Adding a New Modifier

1. Add an entry to `ModifierType` enum in `Common/GlobalNPCs/EnemyModifier.cs`
2. Add 10 `Tier` entries to `TierDatabase.modifierTierDatabase` in `Common/Database/TierDatabase.cs`
3. Add the stat effect in `NPCManager.PreAI()`. All modifier effects belong in the one-time `PreAI` block (guarded by `statChanged`). There is no `PostAI` override — per-tick velocity manipulation was removed because it compounds uncontrollably for accumulation-based NPC AI.
4. Add an `OnHitPlayer` case in both `NPCManager` and `ProjectileManager` for debuffs applied on hit

**Removing a modifier:** Delete its enum value from `ModifierType`, remove its entry from `TierDatabase.modifierTierDatabase`, and remove any switch cases in `PreAI` and `OnHitPlayer`. NPCs don't persist to disk and both sides of a multiplayer session always share the same mod version, so there is no save-migration or network-sync concern from renumbering the enum.

### Adding a New Rarity

Add a row to both `RarityDatabase.rarityModifierDatabase` (3-element list: HP%, defense%, damage% magnitudes) and `rarityWeightDatabase` (8-element weight list matching the 8 boss milestone columns in `GetWeightIndex()`). Weights across all rarities must sum to 100 per column.

## NPC Fields — Scaling & Power Level

Key NPC fields relevant to enemy scaling. Read these in `PreAI` before applying multipliers — by then all mods' `SetDefaults` hooks have run and variant-specific stats (negative netID NPCs) are finalized.

| Field          | Type  | Purpose                        | Notes                                                                                                |
| -------------- | ----- | ------------------------------ | ---------------------------------------------------------------------------------------------------- |
| `npc.lifeMax`  | int   | Max health                     | Use this, not `npc.life`, for baseline                                                               |
| `npc.damage`   | int   | Contact/projectile damage stat | Vanilla baseline before PreAI scaling                                                                |
| `npc.defense`  | int   | Vanilla defense                | Zeroed by `NPCManager.ModifyIncomingHit` so all damage reduction goes through the elemental pipeline |
| `npc.npcSlots` | float | Spawn weight contribution      | Bosses ≈ 6f, mini-bosses ≈ 2–3f, normal enemies = 1f, critters = 0.1–0.25f                           |
| `npc.value`    | float | Coin drop value (in copper)    | Rough economy proxy; set by vanilla per enemy type                                                   |

### Negative netID NPCs

Many vanilla NPCs have negative netIDs (variant NPCs — e.g. slime variants −10 to −5, zombie variants −55 to −26). `npc.netID` is assigned **after** `SetDefaults` completes, and variant-specific stats are finalized after that. Consequences:

- **`SetDefaults`**: cannot check `npc.netID` (not yet assigned); variant-specific stats may be overwritten after this hook returns.
- **`OnSpawn`**: unreliable for negative-netID NPCs — level/stat changes set here will not apply to those variants.
- **`PreAI` + `statChanged` flag** is the correct pattern for one-time stat application. By PreAI time `npc.netID` is stable and all variant stats are settled.

Pattern: roll level/rarity in `SetDefaults` (no netID dependency). Apply stat multiplications in `PreAI` before `statChanged = true`.

**`npc.rarity` is NOT a power-level indicator.** It is the Lifeform Analyzer detection priority (values 0–4), used only to decide which creature to display when multiple rare enemies are nearby. Do not use it for difficulty or coefficient calculations. Modders do not reliably set it.

### Cross-Mod Integration Points

- **`Common/Utils.cs`** — `GetXPMultiplier(rarity, level, modifierCount)`: integration point for `ARPGCharacterSystem`'s XP math. Initially returns the same value as `GetCoinMultiplier`; symbols are separate so coin and XP yields can diverge.
