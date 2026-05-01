# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Development

This is a tModLoader mod for Terraria targeting .NET 8. To build:
- **In-game (recommended):** tModLoader → Workshop → Mod Sources → select `ARPGEnemySystem` → Build & Reload
- **CLI compile check:** `dotnet build` (verifies compilation but does not deploy)

There are no automated tests. Testing requires running tModLoader with the mod loaded.

## In-game Debug Commands

- `/resetworld` — resets the world's `levelCap` and boss progression tracking to zero
- `/setlevelcap <value>` — sets `WorldManager.levelCap` directly for testing high-level enemy spawns

## Cross-Mod Dependency

ARPGItemSystem depends on ARPGEnemySystem (one-way). ARPGEnemySystem has **zero** knowledge of ARPGItemSystem. All elemental system code in this mod is gated on `ModLoader.HasMod("ARPGItemSystem")` — when that mod is absent, vanilla defense math is preserved and no elemental properties are assigned to enemies.

## Architecture

### High-Level Concept

Every non-boss, non-critter NPC is assigned a **level** and **EnemyRarity** on spawn, plus 0–2 random **EnemyModifiers**. Bosses receive a level only. The level cap grows with boss progression and is persisted per-world. The vanilla prefix/reforge system is not touched by this mod; it affects NPCs, not items.

### Core Data Flow

```
WorldManager.levelCap
       ↓ (consumed by)
NPCManager.SetDefaults()  → rolls level + modifiers + elemental properties (if ARPGItemSystem loaded)
BossManager.OnSpawn()     → rolls level + elemental properties (progression-tiered, if ARPGItemSystem loaded)
       ↓ (applied in)
NPCManager.PreAI()        → scales base stats once (statChanged flag prevents re-entry)
BossManager.OnSpawn()     → scales stats immediately on spawn
NPCManager.ModifyIncomingHit() → zeroes vanilla NPC defense (if ARPGItemSystem loaded)
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

| Constant | Value | Purpose |
|---|---|---|
| `PhaseRates` | `{0.003, 0.007, 0.015, 0.030}` | HP/damage scaling per phase |
| `DefPhaseRates` | `{0.004, 0.010, 0.020, 0.040}` | Defense scaling per phase (steeper) |

Server config knobs (tunable per-server):

| Config field | Default | Purpose |
|---|---|---|
| `ScalingExponent` | `1.2` | HP/damage curve shape |
| `DefScalingExponent` | `1.3` | Defense curve shape (steeper than ScalingExponent) |
| `DefenseFloor` | `0.15` | Additive min defense = level × floor |

**Reference values** (HP multiplier at key milestones with defaults):
- Level 50, phase 0 (pre-HM): 1.33× (+33%)
- Level 50, phase 1 (post-WoF): 1.76× (+76%)
- Level 100, phase 2 (post-mechs): 4.77× (+377%)
- Level 150, phase 3 (post-Plantera): 12.91× (+1191%)

### Phase System

`WorldManager.GetScalingPhase()` returns 0–3:
- Phase 0 — pre-hardmode
- Phase 1 — post-WoF (`Main.hardMode`)
- Phase 2 — post-all-three-mechs (`NPC.downedMechBoss1 && downedMechBoss2 && downedMechBoss3`)
- Phase 3 — post-Plantera (`downedBossIDs.Contains(NPCID.Plantera) || NPC.downedPlantBoss`)

Phase changes produce discrete difficulty jumps: the same level enemy becomes significantly harder after each milestone. PhaseRates are the "bump" — same level, different rate, higher multiplier.

### Key Files

- **`Common/Systems/WorldManager.cs`** — Tracks unique boss kills (`downedBossIDs`) and `levelCap` (`bosses downed × LevelCapIncreasePerBossDowned`). Persists to world save via `TagCompound` and syncs in multiplayer via `NetSend`/`NetReceive`. Owns all hardcoded scaling constants (`PhaseRates`, `DefPhaseRates`) and `GetScalingPhase()`. `DownedBoss(npc)` is the primary kill-registration path (called from `BossManager.OnKill`). `SyncDownedFlags(announce)` is a supplemental vanilla-flag backfill that catches multi-segment boss kills (EoW especially) where the last dying segment has `npc.boss == false`; called from `BossFlagSync.OnKill` (live) and `LoadWorldData` (on world load).
- **`Common/GlobalNPCs/BossFlagSync.cs`** — Trivial `GlobalNPC` (applies to all NPCs) that calls `WorldManager.SyncDownedFlags(announce: true)` in `OnKill`. Safety net for vanilla multi-segment bosses; modded bosses go through `BossManager.OnKill` unchanged.
- **`Common/GlobalNPCs/NPCManager.cs`** — `GlobalNPC` for all regular enemies. Stores `level`, `rarity`, `modifierList`, plus elemental fields: `ElementalDamageType`, `ElementalDamagePct`, `FireResistance`, `ColdResistance`, `LightningResistance`. Elemental rolling is gated on `ModLoader.HasMod("ARPGItemSystem")`. `ModifyIncomingHit` zeroes vanilla defense (also gated). Sync appends 5 elemental values after existing fields: `(byte)ElementalDamageType`, then 4 floats — read order must match write order exactly.
- **`Common/GlobalNPCs/BossManager.cs`** — `GlobalNPC` for bosses only. Level + stat scaling applied in `OnSpawn` (safe for bosses — no negative netID variants). Calls `WorldManager.DownedBoss()` on kill. Elemental properties are progression-tiered: pre-WoF=25%, post-WoF=50%, post-Plantera=75% (all elemental resistances + damage %). `PhysicalResistance` is NOT stored — derived at hit time via `ConvertDefenseToResistance(npc.defense, PhysResHalfPoint, ElementalResistanceCap)`.
- **`Common/GlobalNPCs/Rarity.cs`** — `EnemyRarity` struct + `RarityDatabase`. Five rarities (Common / Uncommon / Rare / Elite / Legend). Roll weights (in `rarityWeightDatabase`) shift toward higher rarities across 8 columns, each tied to a boss milestone in `GetWeightIndex()`. Stat bonuses: Common 0/0/0, Uncommon 20/10/10, Rare 50/25/20, Elite 100/50/35, Legend 200/100/60 (HP%/Def%/Dmg%).
- **`Common/GlobalNPCs/EnemyModifier.cs`** — `EnemyModifier` struct + `ModifierType` enum. An excludeList passed to `GenerateModifier` prevents duplicate modifier types on the same enemy.
- **`Common/Database/TierDatabase.cs`** — Static dictionary: `ModifierType → List<Tier>(10 entries)`. Tier 0 = highest values, tier 9 = lowest. `Utils.GetTier()` returns an index based on boss progression (minimum and maximum tier both shrink as more bosses die).
- **`Common/DrawEffects/ModifierDrawEffect.cs`** — Dust particle helpers called from `NPCManager.DrawEffects()`. Each modifier with a visual has its own method.
- **`Common/UI/UISystem.cs`** + **`Common/UI/NPCTooltip.cs`** — `UISystem` (ModSystem) hooks into `ModifyInterfaceLayers` to draw the overlay. `NPCUI` (UIState) rebuilds a `UITextPanel` on every update tick. Now shows: level, rarity, modifiers, defense, Phys Res (computed via `ConvertDefenseToResistance(npc.defense, PhysResHalfPoint, ElementalResistanceCap)`), Fire/Cold/Lightning Res, and elemental damage type/pct. Controlled by `ConfigClient.EnableEnemyStatPanel`.
- **`Common/Elements/Element.cs`** — `Element` enum (`Physical=0`, `Fire=1`, `Cold=2`, `Lightning=3`), byte-backed for efficient serialization.
- **`Common/Elements/ElementalMath.cs`** — Static helpers: `ClampResistance(raw, cap)`, `ApplyResistance(damage, res%, cap)`, `ConvertDefenseToResistance(defense, halfPoint, cap)` = `cap × defense / (defense + halfPoint)`. A hyperbolic curve: `halfPoint` is the defense value at which physRes reaches `cap / 2`. The conversion formula is how vanilla `npc.defense` becomes a physical resistance percentage.
- **`Common/Configs/Config.cs`** — Server-side: `LevelCapIncreasePerBossDowned`, `ScalingExponent` (default 1.3, controls HP/damage curve shape), `ModifierAllowed`, plus elemental entries: `ElementalResistanceCap` (default 75), `EnemyElementalChance` (default 67%), `EnemyBaseElementalAllocationPct` (default 25%), `PhysResHalfPoint` (int, default 30, defense value at which physRes reaches cap/2), `PlayerPhysResCap` (int, default 80), `EnemyElemResPerLevel` (default 0.005). Client-side (`ConfigClient`): `EnableEnemyStatPanel`, `EnableElementalDamageLog` (debug toggle for chat hit log).

### Multiplayer Sync

`NPCManager.SendExtraAI/ReceiveExtraAI` syncs level, rarity (as int), and the modifier list as two parallel int arrays (IDs and magnitudes). `BossManager.SendExtraAI/ReceiveExtraAI` syncs level plus the already-modified stat values directly. `ProjectileManager.SendExtraAI/ReceiveExtraAI` syncs the source NPC's `whoAmI` index so the client can look up the NPC's `NPCManager`/`BossManager`.

### Adding a New Modifier

1. Add an entry to `ModifierType` enum in `Common/GlobalNPCs/EnemyModifier.cs`
2. Add 10 `Tier` entries to `TierDatabase.modifierTierDatabase` in `Common/Database/TierDatabase.cs`
3. Add the stat effect in `NPCManager.PreAI()` (and `PostAI` for per-tick effects like Quick speed boost)
4. Add an `OnHitPlayer` case in both `NPCManager` and `ProjectileManager` for debuffs applied on hit
5. Add a `ModifierDrawEffect` method and call it from `NPCManager.DrawEffects()` for visual feedback

### Adding a New Rarity

Add a row to both `RarityDatabase.rarityModifierDatabase` (3-element list: HP%, defense%, damage% magnitudes) and `rarityWeightDatabase` (8-element weight list matching the 8 boss milestone columns in `GetWeightIndex()`). Weights across all rarities must sum to 100 per column.

## NPC Fields — Scaling & Power Level

Key NPC fields relevant to enemy scaling. Read these in `PreAI` before applying multipliers — by then all mods' `SetDefaults` hooks have run and variant-specific stats (negative netID NPCs) are finalized.

| Field | Type | Purpose | Notes |
|---|---|---|---|
| `npc.lifeMax` | int | Max health | Use this, not `npc.life`, for baseline |
| `npc.damage` | int | Contact/projectile damage stat | Vanilla baseline before PreAI scaling |
| `npc.defense` | int | Vanilla defense | Zeroed by ARPGItemSystem's ModifyIncomingHit when loaded |
| `npc.npcSlots` | float | Spawn weight contribution | Bosses ≈ 6f, mini-bosses ≈ 2–3f, normal enemies = 1f, critters = 0.1–0.25f |
| `npc.value` | float | Coin drop value (in copper) | Rough economy proxy; set by vanilla per enemy type |

### Negative netID NPCs

Many vanilla NPCs have negative netIDs (variant NPCs — e.g. slime variants −10 to −5, zombie variants −55 to −26). `npc.netID` is assigned **after** `SetDefaults` completes, and variant-specific stats are finalized after that. Consequences:

- **`SetDefaults`**: cannot check `npc.netID` (not yet assigned); variant-specific stats may be overwritten after this hook returns.
- **`OnSpawn`**: unreliable for negative-netID NPCs — level/stat changes set here will not apply to those variants.
- **`PreAI` + `statChanged` flag** is the correct pattern for one-time stat application. By PreAI time `npc.netID` is stable and all variant stats are settled.

Pattern: roll level/rarity in `SetDefaults` (no netID dependency). Apply stat multiplications in `PreAI` before `statChanged = true`.

**`npc.rarity` is NOT a power-level indicator.** It is the Lifeform Analyzer detection priority (values 0–4), used only to decide which creature to display when multiple rare enemies are nearby. Do not use it for difficulty or coefficient calculations. Modders do not reliably set it.
