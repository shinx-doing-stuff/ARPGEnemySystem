# Enemy Coin Drop Scaling

**Date:** 2026-05-04
**Scope:** ARPGEnemySystem only

## Goal

Regular enemies drop more coins based on their rarity, level, and number of modifiers. Stronger enemies should feel more rewarding to kill without any single factor dominating.

## Non-goals

- Boss coin scaling (deferred — bosses are more complex due to treasure bags; can reuse this formula later via `BossManager`).
- Item drop scaling (coins only for now).
- Config knobs for the formula constants (hardcoded, tune in code).

## Design

### Formula

```csharp
multiplier = 1 + rarityBonus + levelBonus + modifierBonus

rarityBonus   = 1.5 × rarity.magnitude[0] / 100   // 0.0 (Common) → 3.0 (Legend)
levelBonus    = 0.005 × level                       // 0.0 (L0) → ~0.75 (L150)
modifierBonus = 0.1 × modifierCount                 // 0.0 (0 mods) → 0.8 (8 mods)
```

The three bonuses are additive. Reference values:

| Enemy                       | Multiplier |
| --------------------------- | ---------- |
| Common L0, 0 mods           | 1.0×       |
| Common L50, 0 mods          | 1.25×      |
| Rare L50, 2 mods            | ~2.2×      |
| Elite L100, 5 mods          | ~3.2×      |
| Legend L150, 8 mods         | ~5.55×     |

The rarity term preserves the shape of the existing formula (`1 + 1.5 × magnitude[0] / 100`). Level is deliberately minor (0.5% per level) so it can be tuned up later without rebalancing the other factors.

### Implementation

**`Common/Utils.cs`** — new static method:

```csharp
public static float GetCoinMultiplier(EnemyRarity rarity, int level, int modifierCount)
{
    float rarityBonus   = 1.5f * rarity.magnitude[0] / 100f;
    float levelBonus    = 0.005f * level;
    float modifierBonus = 0.1f * modifierCount;
    return 1f + rarityBonus + levelBonus + modifierBonus;
}
```

**`Common/GlobalNPCs/NPCManager.cs`** — in `PreAI`, replace:

```csharp
npc.value *= 1 + 1.5f * rarity.magnitude[0] / 100f;
```

with:

```csharp
npc.value *= Utils.GetCoinMultiplier(rarity, level, modifierList.Count);
```

This sits inside the `statChanged` guard (one-time application), which is correct — `npc.value` should only be scaled once.

## Files Touched

- `Common/Utils.cs`
- `Common/GlobalNPCs/NPCManager.cs`

## Testing (in-game)

- Kill a Common L0 enemy with no modifiers — confirm coin drop matches vanilla `npc.value`.
- Kill a Legend enemy — confirm ~4× coins from rarity alone (level and mod bonuses on top).
- Kill an enemy with 8 modifiers — confirm modifiers contribute ~+80% on top of rarity+level.
- `/setlevelcap 150` → spawn and kill a high-level enemy → confirm level bonus visible in drop.
