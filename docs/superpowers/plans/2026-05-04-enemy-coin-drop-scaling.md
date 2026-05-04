# Enemy Coin Drop Scaling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Scale regular enemy coin drops additively by rarity, level, and modifier count via a new `Utils.GetCoinMultiplier` helper.

**Architecture:** Add one static method to `Utils` that computes the combined multiplier, then replace the existing single-term rarity-only line in `NPCManager.PreAI` with a call to that method.

**Tech Stack:** tModLoader (.NET 8), C#. No automated tests — verification is in-game only (tModLoader has no test harness).

---

### Task 1: Add `GetCoinMultiplier` to `Utils`

**Files:**
- Modify: `ARPGEnemySystem/Common/Utils.cs:73-81`

- [ ] **Step 1: Add the method**

  In `Common/Utils.cs`, insert the following method before the closing brace of the `Utils` class (currently line 81, after `IsDummy`):

  ```csharp
  internal static float GetCoinMultiplier(EnemyRarity rarity, int level, int modifierCount)
  {
      float rarityBonus   = 1.5f * rarity.magnitude[0] / 100f;
      float levelBonus    = 0.005f * level;
      float modifierBonus = 0.1f * modifierCount;
      return 1f + rarityBonus + levelBonus + modifierBonus;
  }
  ```

  The full `Utils` class closing should look like:

  ```csharp
      internal static float GetCoinMultiplier(EnemyRarity rarity, int level, int modifierCount)
      {
          float rarityBonus   = 1.5f * rarity.magnitude[0] / 100f;
          float levelBonus    = 0.005f * level;
          float modifierBonus = 0.1f * modifierCount;
          return 1f + rarityBonus + levelBonus + modifierBonus;
      }
      internal static bool IsDummy(NPC npc)
      {
          var nameSpan = NPCID.Search.GetName(npc.type).AsSpan();
          var index = nameSpan.IndexOf('/');
          if (index != -1)
              nameSpan = nameSpan[index..];
          return nameSpan.ToString().ToLowerInvariant().Contains("dummy");
      }
  }
  ```

- [ ] **Step 2: Verify compilation**

  ```
  dotnet build
  ```

  Expected: build succeeds with no errors.

- [ ] **Step 3: Commit**

  ```
  git add ARPGEnemySystem/Common/Utils.cs
  git commit -m "feat: add GetCoinMultiplier helper to Utils"
  ```

---

### Task 2: Wire `GetCoinMultiplier` into `NPCManager.PreAI`

**Files:**
- Modify: `ARPGEnemySystem/Common/GlobalNPCs/NPCManager.cs:147`

- [ ] **Step 1: Replace the existing coin value line**

  In `Common/GlobalNPCs/NPCManager.cs`, line 147 currently reads:

  ```csharp
  npc.value    *= 1 + 1.5f * rarity.magnitude[0] / 100f;
  ```

  Replace it with:

  ```csharp
  npc.value    *= Utils.GetCoinMultiplier(rarity, level, modifierList.Count);
  ```

  The surrounding context (lines 142–149) should now look like:

  ```csharp
  // Rarity bonus on top of scaled stats
  npc.lifeMax  += (int)(npc.lifeMax  * rarity.magnitude[0] / 100f);
  npc.life      = npc.lifeMax;
  npc.defense  += (int)(npc.defense  * rarity.magnitude[1] / 100f);
  npc.damage   += (int)(npc.damage   * rarity.magnitude[2] / 100f);
  npc.value    *= Utils.GetCoinMultiplier(rarity, level, modifierList.Count);
  ```

- [ ] **Step 2: Verify compilation**

  ```
  dotnet build
  ```

  Expected: build succeeds with no errors.

- [ ] **Step 3: In-game verification**

  Build and reload the mod in tModLoader (Workshop → Mod Sources → ARPGEnemySystem → Build & Reload).

  Use `/setlevelcap 0` and spawn Common enemies — confirm coin drops match vanilla (no bonus at level 0, no modifiers).

  Use `/setlevelcap 100` and find or spawn a Legend enemy with several modifiers — confirm coins are noticeably higher than vanilla. Enable `EnableElementalDamageLog` in client config if you want numeric confirmation in chat; otherwise inspect the dropped coin pile size.

  Reference multipliers to eyeball against:
  - Common L0, 0 mods → 1.0× (vanilla)
  - Common L50, 0 mods → 1.25×
  - Rare L50, 2 mods → ~2.2×
  - Elite L100, 5 mods → ~3.2×
  - Legend L150, 8 mods → ~5.55×

- [ ] **Step 4: Commit**

  ```
  git add ARPGEnemySystem/Common/GlobalNPCs/NPCManager.cs
  git commit -m "feat: scale enemy coin drops by rarity, level, and modifier count"
  ```
