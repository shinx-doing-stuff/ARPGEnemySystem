# Enemy Scaling

How ARPGEnemySystem decides how strong an enemy is. Everything on this page is read from the code as of 2026-10-04 (commit 7c42f59). Constant tables are copied from the source. World levels, checkpoint rows and worked examples are computed, and they depend on the boss roster of the loaded mod set (§2.1); read the live roster size with `/checklevelcap` or the `World level roster: N bosses.` log line. File references point at the place the rule lives.

ARPGEnemySystem requires ARPGItemSystem (checked at load) and Boss Checklist (declared dependency).

Related pages in ARPGCharacterSystem: `DAMAGE_PIPELINE.md` (how a player hit is resolved against these numbers), `DAMAGE_SCALING.md`.

---

## 1. The model in one paragraph

Every combat NPC gets one **profile** when it spawns on the server. A profile is either a **Regular** (level, rarity, 0 to 8 modifiers) or a **Fight Member** (level only, plus the boss elemental package). The profile writes **max life, coin value and sprite scale** to the NPC once. **Damage and defense are never written**; they are scaled every time something reads them, because vanilla AI rewrites those fields mid-fight. Vanilla's own defense step is turned off for every managed NPC; the character mod turns defense into a percentage of physical resistance instead. The level comes from a **world level**, which is a pure function of how many bosses the world has beaten.

What is scaled: max life, contact and projectile damage, coin value, scale (Colossal and Tiny only), elemental resistances, elemental damage shares, elemental penetration, Sundering.

What is **not** scaled: defense by level (defense is vanilla defense), knockback given or taken, movement speed, AI, hitboxes, loot tables, spawn slots, immunities. The mod writes nothing per tick.

### 1.1 The formulas

`trunc` is integer truncation toward zero; every step below truncates. `vanilla` means the value vanilla produced after its own difficulty, Expert tweaks and multiplayer balance.

**Level multiplier** (`e` = `ScalingExponent`, default 1.14). HP uses one table for every enemy, with one rate per phase on the whole `level^e`, so the kill that opens a phase raises every enemy's health at once. Damage uses a piecewise curve with its own per-kind tables:

```
x        = level ^ e
HP       = HpBase + HpRates[phase] x x
Damage   = base + sum over phases k = 0 .. phase of  rate[k] x max(0, min(x, start[k+1]^e) - start[k]^e)
start    = { 0, 94, 137, 147 },  start[4] = infinity        (damage only)

                     base     rate ph0    rate ph1    rate ph2    rate ph3
HP,     every enemy  0.8085   0.004852    0.008248    0.01398     0.02052
Damage, Regular      1        0.003       0.006       0.010       0.015
Damage, Fight        0.4934   0.009262    0.001879    0           0.006118
```

**Max life** (written once at spawn; `players` applies in Expert and Master to roster bosses and minibosses only):

```
life = trunc(vanilla lifeMax x players x hpMult)
life = life + trunc(life x rarityHP% / 100)                 (Regulars; Common is 0)
life = life + trunc(life x colossal / 150) - trunc(life x tiny / 200)
life = max(1, life)
```

**Damage** (computed on every read of `npc.damage` or `projectile.damage`, so AI rewrites are scaled too):

```
dmg = trunc(vanilla current damage x dmgMult)
dmg = dmg + trunc(dmg x rarityDmg% / 100)
dmg = dmg + trunc(dmg x strong / 100)
```

Difficulty is already inside `npc.damage` (vanilla bakes it in at spawn); it is **not** inside a hostile projectile's damage. Against the player the character mod then applies its own factors: damage variance; for projectiles x2 and the game-mode multiplier (Classic 1, Expert 2, Master 3); and the elemental shares on top (each element adds `dmg x share% / 100`; a phase 3 boss hit is `dmg x 1.48` in raw total).

**Defense** (computed on every read; **no level term**):

```
def = vanilla current defense
def = def + trunc(def x rarityDef% / 100)
def = def + trunc(def x durable / 100)
Fight members: def = vanilla current defense, nothing else.
```

**Physical resistance from defense** (the character mod applies this to every player hit; `h` = `EnemyPhysResHalfPoint`, default 25):

```
d    = def x (1 - min(1, percentPen / 100))     only if def > 0
d    = d - flatPen                              flatPen includes Ichor (5) and Betsy's Curse (13); no floor
res% = min(90, 100 x d / (d + h))               d >= 0
res% = -100 x |d| / (|d| + h)                   d <  0   (vulnerability, no cap)
hit  = hit x (1 - res% / 100)
```

Ichor and Betsy's Curse take off 5 and 13 defense here, a third of vanilla's 15 and 40, because a point of defense is worth far more damage under this ratio rule than under vanilla's.

**Coin value** (Regulars; fight members keep vanilla value):

```
value = vanilla value x (1 + 1.75 x rarityHP% / 100 + 0.005 x level + 0.15 x modifier count)
```

**World level**:

```
worldLevel = 10 + round(190 / N x bossesDowned)          N = Boss Checklist roster size (19 vanilla, Betsy included)
fight level   = uniform [worldLevel, 1.25 x worldLevel)
regular level = uniform [0.75 x worldLevel, 1.1 x worldLevel)
```

Sections 2 to 7 explain every term.

---

## 2. World level

### 2.1 Formula

```
levels per boss = (200 - 10) / N
world level     = 10 + round(levels per boss x bosses downed)
```

`N` is the number of bosses on the Boss Checklist roster: every entry of type Boss with a `downed` delegate. Minibosses and events are not counted. Modded bosses are counted, so `N` depends on the loaded mod set. With vanilla only (Boss Checklist 2.2.4), **`N = 19`** and each boss is worth exactly 10 levels: King Slime, Eye of Cthulhu, Eater of Worlds, Brain of Cthulhu, Queen Bee, Skeletron, Deerclops, Wall of Flesh, Queen Slime, the Twins, Destroyer, Skeletron Prime, Plantera, Golem, **Betsy**, Empress of Light, Duke Fishron, Lunatic Cultist, Moon Lord. Eater of Worlds and Brain of Cthulhu are separate roster entries that share one flag, so beating either counts as two. Betsy counts for the world level although she is a regular enemy in every other respect (§3.1). There is no clamp at 200; the formula reaches 200 exactly when every roster entry is down. (`Common/Scaling/ScalingMath.cs:51-60`, `Common/Systems/BossRoster.cs`)

Boss Checklist is a hard dependency. The mod throws at load if it is missing or the roster call returns null. If a boss's `downed` delegate throws once, that entry is logged and treated as not downed for the rest of the session, but it still counts in `N`, so the world level can then never reach 200.

### 2.2 Checkpoints (vanilla roster, kill order)

| Point in the game | Bosses down | World level | Phase |
|---|---|---|---|
| Fresh world | 0 | 10 | 0 |
| Eye of Cthulhu fight (King Slime down) | 1 | 20 | 0 |
| Wall of Flesh fight (KS, EoC, EoW/BoC x2, QB, Skeletron, Deerclops) | 7 | 80 | 0 |
| Destroyer fight (WoF, Queen Slime down) | 9 | 100 | 1 |
| Plantera fight (all three Mechs down) | 12 | 130 | 2 |
| Moon Lord fight, Betsy not fought | 17 | 180 | 3 |
| Moon Lord fight, Betsy down | 18 | 190 | 3 |
| Everything down | 19 | 200 | 3 |

BalanceLab's checkpoint ladder was authored with `N = 18` (no Betsy): levels 21 / 84 / 105 / 137 / 189. The Phase 4 damage curves were fit at those levels; the HP table was refit at `N = 19`. See the note in §5.1.

### 2.3 Storage and sync

The world level is **not saved**. It is recomputed on world load and every 60 ticks on the server, and loading a world resets it to 10 before the first recompute. On any change the server sends the new value to every client in one packet; clients also receive it on join. Only when it rises does the server also broadcast the chat line "Killing this enemy has released its power upon the world". Clients only display the value; every roll happens on the server. (`Common/Systems/WorldManager.cs`)

### 2.4 Phase

Phase is read from vanilla flags at the moment a profile is rolled, and stored in the profile:

| Phase | Condition |
|---|---|
| 0 | pre-hardmode |
| 1 | hardmode |
| 2 | all three mechanical bosses down |
| 3 | Plantera down |

The phase sets the HP rate for the whole level (so health steps up when the phase advances), changes the slope of the damage curve (§5), and sets the boss elemental tier (§7) and the modifier count (§4.4). It does not change the level roll. (`ScalingMath.cs:62-68`)

---

## 3. Who gets a profile

A profile applies to any NPC that is a boss, or that is not a town NPC, not friendly, not a critter and not the vanilla Target Dummy. Modded dummies are managed. Statue spawns are managed like anything else. (`Common/GlobalNPCs/EnemyProfileNPC.cs:32-33`)

### 3.1 Fights

A **fight** is a boss and everything it spawns. When a boss or a boss-counted NPC spawns without a **fight member** parent (summoned by a player item, spawned naturally, or spawned by a Regular), it rolls a new Fight Member profile. Anything spawned from an NPC or projectile that already carries a Fight Member profile **joins that fight and gets the same profile instance**: same level, same phase, same package. "Spawned from" covers AI spawns, death spawns and on-hit spawns alike. This includes vanilla boss parts (Golem's head and fists, Skeletron's hands, the Hungry, Prime's arms, Plantera's hooks and tentacles, Creepers, Moon Lord's head and hands, Destroyer probes) and anything a boss projectile spawns.

Details worth knowing:

- Level and phase are snapshotted when the fight starts. Members that spawn later reuse the snapshot, even if the world level moved in between.
- **The Twins are two fights.** Retinazer and Spazmatism are spawned by separate calls, so each rolls its own level. The same applies to Mechdusa's parts.
- A fight member is any NPC with vanilla's `boss` flag or in `ShouldBeCountedAsBoss`: the four Celestial Pillars, the Eater of Worlds head and the Torch God. Pillars therefore use the boss curves and have no rarity. The Martian Saucer core carries the `boss` flag, so it is a fight member too.
- Event minibosses without the `boss` flag (Mothron, Pumpking, Ogre, Dark Mage, and so on) are **Regulars**: they roll rarity and modifiers. **Betsy is a Regular** as well: she counts toward the world level because Boss Checklist lists her as a boss, and she gets player-count life (§9.1), but she rolls rarity and modifiers on the Regular curves and gives no boss XP.
- A child of a Regular (a Mother Slime's babies) rolls its own Regular profile; nothing is inherited.

### 3.2 Transform and SetDefaults

When an active NPC re-runs SetDefaults (a Transform, a variant switch, the Eater of Worlds splitting), the profile is carried over, base stats are re-recorded and the life fraction is kept. An unmanaged NPC that transforms into a managed type gets a fresh profile at full life. The **kind is not re-evaluated**: a Regular that becomes a boss type stays a Regular. (`EnemyProfileNPC.cs:121-163`)

### 3.3 Projectiles

Every projectile carries a reference to the profile of the NPC (or projectile) that spawned it. A projectile from a player, a trap or a town NPC has no profile. The reference outlives the shooter. (`Common/GlobalProjectiles/ProjectileManager.cs`)

---

## 4. The profile

### 4.1 Contents

Kind, level, phase, rarity (always None for a fight member), modifiers (always empty for a fight member), the elemental package, an optional scale override (Colossal or Tiny), and the HP and damage multipliers, computed once from the server's `ScalingExponent`. (`Common/Scaling/EnemyProfile.cs`)

### 4.2 Level roll

Uniform, from the current world level, with no input from biome, depth, distance or event:

| Kind | Roll | At world level 137 |
|---|---|---|
| Fight member | `[cap, 1.25 x cap)` | 137 to 170 |
| Regular | `[0.75 x cap, 1.1 x cap)` | 102 to 149 |

The bracket notation above is exclusive at the top; the table below lists the inclusive minimum and maximum actually rolled. (`EnemyProfileNPC.cs:81-82, 89-90`)

| World level | Regular range | Fight range |
|---|---|---|
| 10 | 7 to 10 | 10 to 11 |
| 20 (EoC) | 15 to 21 | 20 to 24 |
| 80 (WoF) | 60 to 87 | 80 to 99 |
| 100 (Destroyer) | 75 to 109 | 100 to 124 |
| 130 (Plantera) | 97 to 142 | 130 to 161 |
| 180 (Moon Lord, no Betsy) | 135 to 197 | 180 to 224 |
| 190 (Moon Lord, Betsy down) | 142 to 208 | 190 to 236 |
| 200 | 150 to 219 | 200 to 249 |

### 4.3 Rarity (Regulars only)

Six values: None (fight members only), Common, Uncommon, Rare, Elite, Legend. A Regular always rolls Common or better; every weight column sums to 100. (`Common/GlobalNPCs/Rarity.cs`)

The weight column is chosen by **counting flags**, one each for: King Slime down, Skeletron down, hardmode, Queen Slime down, any Mech down, Golem down, Plantera down. Skipping a boss keeps every later column one lower. The source labels the columns by the boss it expects at each step, but since the index is a count, in vanilla kill order column 6 is reached when Plantera dies and column 7 when Golem dies.

| Rarity | 0 (fresh) | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---|---|---|---|---|---|---|---|
| Common | 70 | 60 | 55 | 50 | 40 | 30 | 20 | 10 |
| Uncommon | 20 | 25 | 25 | 25 | 25 | 25 | 20 | 15 |
| Rare | 8 | 10 | 13 | 15 | 20 | 22 | 25 | 30 |
| Elite | 2 | 4 | 5 | 8 | 12 | 17 | 25 | 30 |
| Legend | 0 | 1 | 2 | 2 | 3 | 6 | 10 | 15 |

Columns reached at the checkpoints: EoC 1, WoF 2, Destroyer 4, Plantera fight 5, after Plantera 6, after Golem 7.

Stat bonuses, applied after the level multiplier:

| Rarity | HP | Defense | Damage |
|---|---|---|---|
| Common | +0% | +0% | +0% |
| Uncommon | +20% | +10% | +10% |
| Rare | +50% | +25% | +20% |
| Elite | +70% | +40% | +30% |
| Legend | +100% | +70% | +50% |

Elemental baseline by rarity (the Regular's package before modifiers):

| Rarity | Fire/Cold/Lightning res | Chaos res | Fire/Cold/Lightning pen, and Sundering % | Chaos pen |
|---|---|---|---|---|
| Common | 0 | 0 | 0 | 0 |
| Uncommon | 10 | 3 | 5 | 3 |
| Rare | 20 | 5 | 10 | 5 |
| Elite | 30 | 8 | 15 | 8 |
| Legend | 40 | 10 | 20 | 10 |

Rarity gives no elemental **damage**; only modifiers do.

### 4.4 Modifiers (Regulars only)

Eighteen types: Colossal, Tiny, Strong, Durable, SoulDrinker, Flaming, Glacial, Charged, FireResistant, ColdResistant, LightningResistant, Searing, Shattering, Conductive, Sundering, ChaosInfused, ChaosResistant, ChaosPenetrating. No duplicates on one enemy; otherwise the pick is uniform. Colossal and Tiny are different types and **can both roll**. (`Common/GlobalNPCs/EnemyModifier.cs`, `Common/Utils.cs`)

**How many.** Uniform between a floor and `max = min(8, 2 + rarity bonus + phase)`, where the rarity bonus is Common 0, Uncommon 1, Rare 1, Elite 2, Legend 3. The floor is 1 for Elite, 2 for Legend, 0 otherwise.

| Rarity | Phase 0 | Phase 1 | Phase 2 | Phase 3 |
|---|---|---|---|---|
| Common | 0 to 2 | 0 to 3 | 0 to 4 | 0 to 5 |
| Uncommon, Rare | 0 to 3 | 0 to 4 | 0 to 5 | 0 to 6 |
| Elite | 1 to 4 | 1 to 5 | 1 to 6 | 1 to 7 |
| Legend | 2 to 5 | 2 to 6 | 2 to 7 | 2 to 8 |

**How strong.** Each modifier separately rolls a **tier row** of the tier table (`Common/Database/TierDatabase.cs`), then a magnitude uniform inside that row; one enemy can mix rows. Row 0 is the strongest. A fresh world rolls rows 8 to 9. The best (lowest) row index allowed decreases by one, so the roll gets stronger, for each of: King Slime, Skeletron, hardmode, any Mech, Plantera, Empress, Cultist, Moon Lord. The worst (highest) row index allowed decreases by one for each of: Eater/Brain, Queen Slime, Golem, Fishron, Empress, Moon Lord.

| Checkpoint (vanilla kill order, optional bosses included) | Tier rows rolled |
|---|---|
| Fresh | 8 to 9 |
| EoC | 7 to 9 |
| WoF fight | 6 to 8 |
| Destroyer fight | 5 to 7 |
| Plantera fight | 4 to 7 |
| Moon Lord fight | 1 to 4 |
| After Moon Lord | 0 to 3 |

Without Fishron the Moon Lord fight rolls 1 to 5; without Empress 2 to 5; without both 2 to 6.

Magnitude ranges by row (uniform inside the range):

| Modifiers | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 |
|---|---|---|---|---|---|---|---|---|---|---|
| Colossal, Strong, Durable, SoulDrinker | 56-60 | 51-55 | 46-50 | 41-45 | 36-40 | 31-35 | 26-30 | 21-25 | 16-20 | 10-15 |
| Tiny, Fire/Cold/Lightning Resistant, Searing, Shattering, Conductive, Sundering | 36-40 | 33-35 | 30-32 | 27-29 | 24-26 | 21-23 | 18-20 | 15-17 | 12-14 | 10-11 |
| Flaming, Glacial, Charged | 59-65 | 53-57 | 49-52 | 44-47 | 39-43 | 34-38 | 30-33 | 25-27 | 20-23 | 17-18 |
| ChaosInfused | 19-21 | 17-19 | 16-17 | 14-16 | 13-14 | 11-13 | 10-11 | 8-9 | 7-8 | 5-6 |
| ChaosResistant | 9-10 | 8-9 | 8 | 7 | 6-7 | 5-6 | 5 | 4 | 3-4 | 3 |
| ChaosPenetrating | 18-20 | 17-18 | 15-16 | 14-15 | 12-13 | 11-12 | 9-10 | 8-9 | 6-7 | 5-6 |

**What they do** (`m` is the magnitude; every step truncates to an integer):

| Modifier | Effect |
|---|---|
| Colossal | scale = 1 + m/100; max life += life x m/150 |
| Tiny | scale = 1 - m/100; max life -= life x m/200 |
| Strong | damage += damage x m/100 |
| Durable | defense += defense x m/100 |
| Flaming, Glacial, Charged, ChaosInfused | +m% of the hit as that element's damage |
| Fire/Cold/Lightning/Chaos Resistant | +m to that resistance |
| Searing, Shattering, Conductive, ChaosPenetrating | +m to that element's penetration |
| Sundering | +m% Sundering (cuts the player's defense by that share) |
| SoulDrinker | no stat change; each contact or projectile hit drains m mana from the player |

Colossal and Tiny change the sprite and the life, **not the hitbox**. The scale override is absolute, so a variant whose vanilla scale is not 1 has it replaced. If both roll, the last one rolled sets the scale and both life changes apply.

---

## 5. HP and damage curves

### 5.1 The formula

Both multipliers are built from `x = level ^ e`, where `e` is the server config `ScalingExponent` (default 1.14).

**HP** is one table for every enemy kind: the current phase's rate applies to the whole `x`, so the multiplier steps up at once for every enemy when a phase kill advances the phase. Within a phase it rises smoothly with level.

**Damage** is piecewise: each phase has its own slope, and a phase's slope applies only to the part of the level range above that phase's start level, so moving up a level never jumps.

```
HP(level, phase)     = HpBase + HpRates[phase] x x
Damage(level, phase) = base + sum over k = 0..phase of  rate[k] x max(0, to[k] - start[k]^e)
    start = { 0, 94, 137, 147 }          (phase start levels)
    to[k] = min(x, start[k+1]^e)         for k < phase
    to[k] = x                            for k = phase (open-ended)
```

(`ScalingMath.cs:15-49`)

Constants (damage: Phase 4 refit, 2026-10-04; HP: refit 2026-10-15):

| | base | phase 0 | phase 1 | phase 2 | phase 3 |
|---|---|---|---|---|---|
| HP, every enemy | 0.8085 | 0.004852 | 0.008248 | 0.01398 | 0.02052 |
| Damage, Regular | 1 | 0.003 | 0.006 | 0.010 | 0.015 |
| Damage, Fight member | 0.4934 | 0.009262 | 0.001879 | 0 | 0.006118 |

The HP table is solved by BalanceLab so that the reference melee build (`melee-ref-medium`, finished gear at every tier) kills each boss at about 1.10x the vanilla player's speed. Hits taken come from the damage tables, which are the Phase 4 fit and are not refit here. Regular damage rates are design values; regular enemies share the boss HP table.

**Open item (2026-10-04).** BalanceLab's checkpoint ladder was authored with a roster of 18 (it had no Betsy), so the Phase 4 damage fit was solved at world levels 21 / 84 / 105 / 137 / 189. The live roster is 19, which puts the same fights at 20 / 80 / 100 / 130 / 180 (190 with Betsy down). Bosses in game therefore roll a few levels lower than the fit assumed; the gap is largest at Plantera (roll 130 to 161 instead of 137 to 170) and Moon Lord. The phase start levels 94 / 137 / 147 were also chosen against the 18-roster ladder. A damage refit at `N = 19` is pending a decision. The HP table (2026-10-15) was fitted at the live `N = 19` levels, at the middle of each boss's level roll (22 / 90 / 112 / 146 / 213), so for HP this is closed.

### 5.2 Shape of the boss curves

- **Health steps at every phase kill.** The kill that opens a phase raises every enemy's HP multiplier at the same level: 1.35x at level 90 (Wall of Flesh), 1.48x at level 112 (last mechanical boss), 1.39x at level 146 (Plantera). Between kills, health rises smoothly with level.
- **Fight damage is flat at 2.32x in phase 2** (the phase 2 rate is 0), for every level from 137 up until Plantera dies.
- **Fight damage is below 1.0x below level 34** (base 0.4934): Eye of Cthulhu at level 21 hits for 0.79x its vanilla contact damage before elementals. The boss elemental package (+34% at tier 0, §7) brings the raw total back to about vanilla.
- **The damage curve is continuous in level, not across a phase change at a fixed level.** When the phase advances, the old phase's open-ended slope is cut at the next start level and replaced by the new rate. For fight damage this can go down: a level 104 boss hits 2.34x in phase 0 and 2.18x in phase 1. For Regulars every later rate is higher, so their damage multiplier only rises. HP is not continuous across a phase change; it steps up.
- **Plantera's HP depends on the roll.** The Plantera-fight roll spans 130 to 161 (world level 130): 4.40x at the bottom, 5.39x at the top. Plantera's damage is 2.32x on every roll.

Multiplier table (default exponent):

| Level | Phase | HP, every enemy | Regular Dmg | Fight Dmg |
|---|---|---|---|---|
| 10 | 0 | 0.88 | 1.04 | 0.62 |
| 20 | 0 | 0.96 | 1.09 | 0.78 |
| 21 | 0 | 0.96 | 1.10 | 0.79 |
| 50 | 0 | 1.23 | 1.26 | 1.29 |
| 80 | 0 | 1.53 | 1.44 | 1.86 |
| 84 | 0 | 1.57 | 1.47 | 1.94 |
| 104 | 0 | 1.78 | - | 2.34 |
| 100 | 1 | 2.38 | 1.61 | 2.16 |
| 105 | 1 | 2.47 | 1.68 | 2.18 |
| 130 | 1 | 2.93 | - | 2.29 |
| 130 | 2 | 4.40 | 2.01 | 2.29 |
| 137 | 2 | 4.62 | 2.10 | 2.32 |
| 161 | 2 | 5.39 | - | 2.32 |
| 170 | 2 | 5.69 | - | 2.32 |
| 150 | 3 | 7.02 | 2.44 | 2.36 |
| 180 | 3 | 8.45 | 3.48 | 2.79 |
| 189 | 3 | 8.89 | 3.80 | 2.92 |
| 190 | 3 | 8.94 | 3.84 | 2.93 |
| 200 | 3 | 9.43 | 4.20 | 3.08 |
| 235 | 3 | 11.16 | - | 3.60 |

HP spread from the fight level roll alone, at the live world levels (`N = 19`): EoC 20 to 24 0.96 to 0.99x, WoF 80 to 99 1.53 to 1.72x, Destroyer 100 to 124 2.38 to 2.82x, Plantera 130 to 161 4.40 to 5.39x, Moon Lord 180 to 224 8.45 to 10.61x (Betsy not down) or 190 to 236 8.94 to 11.21x (Betsy down).

### 5.3 Order of operations

Level first, then rarity (Regulars), then each modifier in roll order. Every step truncates toward zero. (`EnemyProfile.cs:74-81`, `Common/Scaling/EnemyScaling.cs`)

**Max life (written once at spawn)**
1. Start from vanilla's max life after vanilla's own difficulty, Expert tweaks, multiplayer balance, and this mod's player-count scaling (§9.1).
2. x HP multiplier.
3. + rarity HP%.
4. Colossal + life x m/150, Tiny - life x m/200.
5. At least 1. Life is set to the new max.

**Damage (computed at read time)**
1. vanilla's *current* `npc.damage` (difficulty already inside) or `projectile.damage` (difficulty not inside; the character mod adds it, §8) x damage multiplier.
2. + rarity damage%.
3. Strong + damage x m/100.

**Defense (computed at read time)**
- vanilla's *current* `npc.defense` + rarity defense%, then Durable + defense x m/100.
- Fight members: vanilla defense exactly.
- Negative defense stays negative and grows in magnitude with rarity.

**Coin value (written once)**: Regulars x `(1 + 1.75 x rarityHP%/100 + 0.005 x level + 0.15 x modifier count)`. Fight members keep vanilla value.

Because damage and defense are read-time, bosses whose AI rewrites those fields every frame (Plantera, Skeletron, Prime, Duke, Empress, Golem) are scaled correctly at every moment.

### 5.4 Worked examples

Rows are indexed by the rolled level, which is what the formula sees; see §4.2 for which world level rolls which range.

**Zombie** (45 life, 14 damage, 6 defense, Classic):

| Rolled level | Phase | Rarity | Life | Damage | Defense |
|---|---|---|---|---|---|
| 10 | 0 | Common | 39 | 14 | 6 |
| 21 | 0 | Common | 43 | 15 | 6 |
| 22 | 0 | Legend | 86 | 22 | 10 |
| 141 | 3 | Common | 296 | 30 | 6 |
| 189 | 3 | Common | 399 | 53 | 6 |

Expert doubles the raw life and damage before this mod runs, and Expert hardmode also applies vanilla's pre-hardmode-enemy boost to life, damage, **defense** and coin value. The mod multiplies whatever vanilla produced.

**Armored Skeleton** (260 / 40 / 28, Common):

| Rolled level | Phase | Life | Damage | Defense |
|---|---|---|---|---|
| 78 | 1 | 518 | 57 | 28 |
| 105 | 1 | 642 | 67 | 28 |
| 137 | 2 | 1201 | 84 | 28 |
| 189 | 3 | 2310 | 152 | 28 |

With rarity: Elite 39 defense, Legend 47, Legend with Durable 60: 75.

**Bosses** (Expert solo raw, at the Phase 4 ladder levels, `N = 18`; the live `N = 19` levels are about 5% lower, §2.2):

| Boss | Ladder world level | Level roll | Life raw to scaled | Contact damage raw to scaled |
|---|---|---|---|---|
| Eye of Cthulhu | 21 | 21 to 25 | 3640 to 3510 .. 3635 | 30 to 23 .. 25 |
| Wall of Flesh | 84 | 84 to 104 | 11200 to 17543 .. 19883 | 150 to 291 .. 350 |
| Destroyer | 105 | 105 to 130 | 120000 to 296401 .. 351366 | body 93 to 203 .. 212 |
| Plantera | 137 | 137 to 170 | 42000 to 194140 .. 238822 | 100 to 231 (every roll, first form) |
| Moon Lord | 189 | 189 to 235 | core 75000 to 666543 .. 837342 | True Eye 120 to 350 .. 431 |

Plantera's AI rewrites its contact damage and defense by form: 100 damage / 36 defense in the first form, 140 damage / 10 defense below half life (Expert), so the scaled second-form damage is 324 and the defense read at that moment is 10. Every part, segment, projectile and minion of a fight uses the same multiplier. Destroyer segments share the head's life pool through vanilla's `realLife`. Fight member defense is vanilla's current value (Moon Lord head 50, hands 40, core 70).

---

## 6. Defense and armour

### 6.1 Vanilla defense is turned off

For every hit on a managed NPC, from any source, the mod multiplies the hit's defense modifier by 0 and cancels vanilla's bonus damage for negative defense. Vanilla's flat debuffs (Ichor -15, Betsy's Curse -40) survive the multiply but have no effect, because vanilla clamps defense at 0 before subtracting. Nothing is double counted. (`EnemyProfileNPC.cs:196-201`)

Only the character mod's player hit path puts defense back, as physical resistance (§6.2). **Hits from any other source ignore enemy defense entirely**: town NPCs, NPC against NPC, traps, and the character mod's thorns. The reverse gap exists for unmanaged NPCs (critters, town NPCs, the Target Dummy): the player hit path converts their raw defense to resistance while vanilla's defense step still runs, so a player hitting one pays defense twice. Low impact.

### 6.2 Defense to physical resistance

The enemy mod provides the numbers and the conversion; the character mod applies them.

```
d >= 0:  res% = min(90, 100 x d / (d + h))
d <  0:  res% = -100 x |d| / (|d| + h)        (no cap; tends to -100%, i.e. double damage)
```

`h` is the server config `EnemyPhysResHalfPoint`, default 25, the same for every enemy: an enemy with 25 defense resists half. The ceiling of 90% is reached at 225 defense. There is no floor on defense. (`Common/Elements/ElementalMath.cs:31-34`, `Common/GlobalNPCs/EnemyStats.cs`)

Effective defense, on the character mod side, in this order:
1. `d = EnemyStats.Defense(target)` (vanilla current defense with rarity and Durable).
2. If `d > 0`: `d x= 1 - min(1, percent pen)`. Percent pen is the weapon and player percent armour penetration, plus vanilla's "ignore all positive defense" cases counted as 100%: the Lightning Aura sentry (not against the Dungeon Guardian) and Flymeal against town-type NPCs.
3. `d -= flat pen`, with no floor. Flat pen is the sum of weapon and player flat armour penetration, vanilla projectile armour penetration, +5 for Strong Bees wasps, and Ichor / Betsy's Curse. A direct strike (no item or projectile) uses the player's total plus the weapon's own penetration. Armour penetration that comes from vanilla (the weapon's and projectile's own, accessories and buffs such as the Sharpening Station, Strong Bees) counts at one third (`ScalingDefaults.VanillaArmorPenScale`), because vanilla sized those numbers for a rule where a point removes half a point of damage, while here a point of defense is worth 1.3% to 4% of damage. Ichor and Betsy's Curse are cut to a third in `EnemyStats` itself (5 and 13).
4. Convert; apply as `damage x (1 - res/100)`.

Reaps (the character mod's stored-damage hits) count only Ichor and Betsy's Curse as vanilla penetration.

Worked examples (`h = 25`):

| Target (defense) | 0 pen | 8 flat pen | 13 flat pen |
|---|---|---|---|
| Zombie (6) | 19.4% | -7.4% | -21.9% |
| Armored Skeleton (28) | 52.8% | 44.4% | 37.5% |
| Moon Lord head (50) | 66.7% | 62.7% | 59.7% |
| Moon Lord core (70) | 73.7% | 71.3% | 69.5% |
| Moon Lord hands (40) | 61.5% | 56.1% | 51.9% |

Zombie with Ichor: defense 6 - 5 = 1, so 100 x 1 / (1 + 25) = 3.8% resistance and 0.96x damage (with vanilla's 15 it was -26.5%, 1.26x). A Legend Armored Skeleton (47) resists 65.3%; with Durable 60 (75 defense), 75%. The gain from flat penetration peaks where the target's defense is about equal to the penetration and fades on both sides.

---

## 7. Elemental package

Elements are Fire, Cold, Lightning and Chaos. Physical is the base hit.

### 7.1 Regulars

The rarity baseline (§4.3) plus modifiers (§4.4). Nothing depends on level or phase. The baseline gives Fire, Cold and Lightning one shared value with Chaos lower; single-element modifiers (FireResistant, Searing, Flaming, and so on) then move one element on its own.

### 7.2 Fight members

One package per tier, chosen by phase. Every boss deals all three elements plus Chaos at once, on purpose. (`EnemyScaling.cs:122-157`)

| Tier (phases) | F/C/L res | Chaos res | F/C/L damage % (each) | Chaos damage % | F/C/L pen | Chaos pen | Sundering % |
|---|---|---|---|---|---|---|---|
| 0 (phase 0) | 25 | 6 | 10 | 4 | 5 | 3 | 15 |
| 1 (phases 1 and 2) | 50 | 13 | 12 | 5 | 10 | 5 | 30 |
| 2 (phase 3) | 75 | 19 | 14 | 6 | 15 | 8 | 45 |

### 7.3 Caps and penetration

- Resistance cap 75% (`ElementalMath.ElementCap`). It applies at hit time, not when the package is built: a Legend with FireResistant can store 80 and the panel shows 80, but 75 applies.
- Player elemental penetration subtracts from the **stored** resistance first, and the cap applies after: `effective = min(75, stored - pen)`. So resistance stored above the cap absorbs penetration before it does anything; against 80 stored fire resistance, 0 and 5 penetration deal the same damage. There is no floor, so penetration beyond the resistance makes the enemy vulnerable. Fire, Cold and Lightning penetration include the "all elemental penetration" affix; Chaos does not. The same order applies to enemy penetration against the player's resistances.
- Against the player, enemy penetration subtracts from the player's resistance the same way. Sundering cuts the player's defense by its percentage before the player's own physical resistance is computed.

---

## 8. Damage to the player

The character mod's hurt pipeline owns this; the enemy mod supplies the scaled damage and the package.

- **Contact**: base = vanilla damage variance of `EnemyStats.ContactDamage(npc)`. Difficulty is already inside vanilla's `npc.damage`.
- **Enemy projectile** (the projectile carries a profile): base = variance of `EnemyStats.ProjectileDamage(proj)` x 2, x 0.7 for Warmth against cold projectiles, x the game mode's enemy damage multiplier (Classic 1, Expert 2, Master 3, or the Journey slider). Reflected projectiles and types in vanilla's `PlayerHurtDamageIgnoresDifficultyScaling` set skip the difficulty step. Projectiles without a profile (traps, boulders) use vanilla math.
- The physical portion is the whole base. **Each element adds on top**: a phase 3 boss hit of 100 base is 100 physical + 14 Fire + 14 Cold + 14 Lightning + 6 Chaos = 148 before the player's resistances.
- Then the player's physical resistance (half point `PhysResHalfPoint`, default 60, cap 80 plus bonuses), elemental resistances minus enemy penetration, "less damage taken", Composure, deflect and mana absorb, and vanilla's endurance-type reductions (Endurance, solar shield, beetle, paladin).
- The character mod sets the final damage itself, so vanilla's other incoming-damage multipliers are **not** applied to these hits: banner damage reduction, Warmth against cold **contact** hits, and any other mod's hit-by-NPC or hit-by-projectile multipliers.

Note that the player's "half point" works differently from the enemy's (§6.2): the player curve is `cap x d / (d + h)`, so at 60 defense the player has 40% physical resistance (half of the 80 cap), while an enemy at its half point has 50%.

So enemy damage against the player = level multiplier x rarity x Strong on vanilla's current raw value, for contact and projectiles alike, plus the elemental shares. Bosses gain about +34% / +41% / +48% raw from the package by tier.

---

## 9. Multiplayer

### 9.1 Player-count life scaling

The mod detours vanilla's `ScaleStats`, calls it as if for one player, and then multiplies max life by the real player count whenever vanilla itself would have scaled for players: Expert and Master, Journey with the difficulty slider at 2x or more, and Classic "For the Worthy" worlds. It applies only to NPC types on Boss Checklist's boss **and miniboss** entry lists. **Only max life** changes. The count is frozen at spawn. This runs before the enemy mod records base life, so the level multiplier multiplies the player-scaled life. Example: four players, Plantera at level 161: 42000 x 4 x 5.57 = about 936 000. (`Common/Systems/BossPlayerScaling.cs`)

Which NPCs are on those lists:
- Every roster boss's main body. Moon Lord's entry lists the head as well as the core, so the head gets full scaling and only the hands keep vanilla's partial scaling. Eater of Worlds lists head, body and tail, so every segment (each with its own life pool) gets full scaling.
- Limbs registered as limbs (Skeletron's hands, Prime's arms, Golem's head and fists) are not on the list and keep vanilla's partial scaling. Destroyer segments share the head's pool anyway.
- Minibosses: Dark Mage, Ogre, Flying Dutchman, Mourning Wood, Pumpking, Everscream, Santa-NK1, Ice Queen, Martian Saucer, and Betsy. Those without the vanilla `boss` flag are Regulars, so rarity and modifiers stack on top of the player-count life. Mothron is not on the list and gets no player-count life.

With `EnableBossScalingLog` on, the server broadcasts one chat line per listed type when it spawns (throttled to once per 60 ticks per type; only from a fresh spawn, not after a Transform): solo life, life after player scaling, final life, level. In a mode without player scaling the line says so instead.

### 9.2 Profile sync

There is no custom profile packet. The profile rides on vanilla's NPC sync (spawn and `netUpdate`) as extra AI: a has-profile bit, a life-is-full bit, then the level (7-bit), a header byte (kind, phase, rarity), the modifier count and each modifier's type and magnitude. Projectiles send the same codec plus the shooter index. The client rebuilds the profile from the data and the synced server config and applies the writes (max life, value, scale). The world level is the only custom packet. (`Common/Scaling/EnemyProfileCodec.cs`, `EnemyProfileNPC.cs:165-192`)

### 9.3 What runs where

| Code | Single player | Server | MP client |
|---|---|---|---|
| World level recompute, both commands | yes | yes | command runs on the server |
| Spawn roll and writes | yes | yes | no |
| SetDefaults carry-over | yes | yes | no |
| Player-count life scaling | yes | yes | yes |
| Profile rebuild from sync | - | - | yes |
| Defense zeroing | wherever the hit is computed | | |
| SoulDrinker mana drain | local | - | the hurt player's client |
| Stat panel | yes | - | yes |

### 9.4 Known gaps

- Between spawn and the first sync packet, a client's NPC has no profile: stats read as vanilla, the character mod sees 0 elemental resistance and falls back to vanilla hurt math, and `GetEnemyInfo` returns `{0, 0}`.
- Changing `ScalingExponent` mid-session: existing profiles on the server and on already-connected clients keep their multiplier (the sync data has not changed, so the client keeps its instance), but a client that joins later rebuilds every NPC with the new value, so max life can differ between machines until the NPC despawns.

---

## 10. Config

Server side (`Common/Configs/Config.cs`):

| Field | Default | Range | What it changes |
|---|---|---|---|
| `ScalingExponent` | 1.14 | 1.0 to 2.0 | `e` in `level^e` for HP and damage, both kinds. Baked into each profile when it is built. |
| `EnemyPhysResHalfPoint` | 25 | 5 to 100 | `h` in the enemy defense to resistance curve, for every enemy. |
| `PhysResHalfPoint` | 60 | 1 to 200 | The **player's** half point; read by the character mod. |
| `EnableBossScalingLog` | off | | Boss chat line (§9.1). |

Client side: `EnableEnemyStatPanel` (§11.2), `EnableElementalDamageLog` and `EnableReapLog` (character mod chat logs).

Notes:
- `DefenseFloor` and `DefScalingExponent` no longer exist. A saved config that still has them loads fine; the keys are ignored and dropped on the next save.
- Ranges are enforced by the config UI only. A hand-edited value outside the range loads as is; an `EnemyPhysResHalfPoint` of 0 breaks the conversion for 0-defense enemies.

---

## 11. Debug commands and the stat panel

### 11.1 Commands

- `/checklevelcap`: "World level {cap} - {downed}/{N} bosses downed, {levels each}", the hold if any, then one line per downed roster entry.
- `/setlevelcap <value>`: holds the world level for new spawns, sends the new value to every client and replies to the caller only (no chat broadcast). `/setlevelcap auto` releases it. The hold is not saved. There is no permission check and no upper bound. **It changes only the level**: phase, rarity column, modifier count and tier rows still come from boss flags, so `/setlevelcap 189` in a fresh world gives level-189, phase-0 enemies with fresh-world rarity and tier 8 to 9 modifiers. `/setlevelcap 0` rolls level 1.

### 11.2 Enemy stat panel (`EnableEnemyStatPanel`)

Drawn at the top centre of the screen for the NPC under the mouse (hit test on the sprite frame at the NPC's feet; the last matching NPC wins; NPCs without a profile are skipped). Lines:

1. Name.
2. `Level {L}` for a fight member; `Level {L}  {Rarity}  [{modifiers}]` for a Regular with modifiers; `Level {L}  {Rarity}` for one without.
3. `Damage: {scaled contact damage}   Defense: {defense minus Ichor/Betsy} (Phys Res {x}%)`. The resistance uses the config half point and ignores the viewer's penetration. Damage includes difficulty but not variance, the projectile x2, or elementals.
4. `Resistances: Fire Cold Lightning Chaos`, raw and uncapped.
5. `Elem Dmg:` shares above 0, or `none`.
6. `Penetration:` only if any value is above 0.

Max life is not shown. Rarity and modifier names are the raw enum names.

---

## 12. API for other mods

`Call("GetEnemyInfo", NPC npc)` returns `int[] { level, rarityTier }` for a managed NPC (rarity 0 for fight members), `{0, 0}` when managed but not yet synced to this client, and `null` for an unmanaged NPC. Fancy Healthbar reads it for its level badge and rarity frame. Keep the shape stable or version the name. (`ARPGEnemySystem.cs:26-42`)

The character mod references this mod at compile time and reads `EnemyStats` (profile, defense, contact and projectile damage, half point, debuff defense reduction), `ProjectileManager` (the projectile's profile and the shooter index, used by its hurt log and the kaeshi counter-strike), `ElementalMath`, the `Element` enum and the config half points directly. Its XP award is `life/5 + contact damage x 2 + defense x 2`, x5 for NPCs with the vanilla `boss` flag (so not Eater of Worlds segments, pillars or Betsy), x the coin-style multiplier for Regulars, x a level-difference factor; no XP for dummies, critters or statue spawns, and a 70% share when teammates split it. Since max life includes the player-count scaling, boss XP grows with the player count.

BalanceLab links the scaling source files directly and mirrors the rest. It models a Regular as rarity None with no modifiers, a state that never occurs in game, so its regular-enemy numbers are a floor.

---

## 13. Quirks to know about

Factual observations from the code, listed so nobody rediscovers them. None of these is a decision.

- Fight member damage can fall when the phase advances at a fixed level (§5.2). HP never falls; it steps up.
- Boss damage is below 1.0x under level 34; the elemental package compensates in raw total.
- The Twins and Mechdusa's parts roll separate levels.
- Celestial Pillars and the Torch God are fight members; event minibosses are Regulars with rarity and modifiers, and also get player-count life.
- Defense is zeroed for every hit source but restored only for player hits; thorns, traps, town NPCs and NPC-vs-NPC hits ignore it.
- Colossal and Tiny can co-roll; their scale is absolute and the hitbox is not resized.
- SoulDrinker subtracts mana without a clamp at 0.
- `/setlevelcap` has no permission check or upper bound and is not persisted.
- A transformed NPC keeps its old kind.
- The panel shows raw enum names, uncapped resistances, resistance without the viewer's penetration, and uses the unscaled frame for the hover test.
- Betsy counts as a boss for the world level and gets player-count life, but is a Regular enemy with rarity and modifiers and no boss XP. The Martian Saucer core is a fight member.
- The rarity bonus table has no `None` row; a Regular with rarity None would throw in the coin multiplier and in the character mod's XP multiplier. It cannot happen through the normal roll.
- Regular rolls use `System.Random`; the level uses `Main.rand`. All on the server, so not a sync problem.
