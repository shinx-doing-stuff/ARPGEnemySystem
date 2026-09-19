# World Level Scaling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the boss-count-based `levelCap` with a fixed 10–200 range derived from Boss Checklist's deduplicated boss roster, so world level means the same thing regardless of how many content mods are installed.

**Architecture:** A new `BossRoster` ModSystem pulls every `isBoss` entry from Boss Checklist once at load, keeping each entry's `downed` predicate. `WorldManager` polls those predicates on the server once a second and sets `levelCap = 10 + (190 / N) × downed`. All kill-registration, the persisted boss-ID list, and the `BossFlagSync` safety net are deleted — counting downed *states* rather than kill *events* makes the existing Twins/Moon Lord double-counting impossible rather than merely patched.

**Tech Stack:** C# / .NET 8, tModLoader 1.4.4, Boss Checklist mod-call API version `1.6`.

**Spec:** `docs/superpowers/specs/2026-09-19-world-level-scaling-design.md`

## Global Constraints

- **Branch:** `feat/world-level-scaling` already exists with the spec committed. Do not create a new branch.
- **No test harness.** This project has no automated tests. Every task's verification step is `dotnet build` plus, where noted, an item on the in-game checklist in Task 4. Never claim behaviour is verified from a build alone.
- **Build expectations.** `dotnet build` in this repo ends with `TML003` and `MSB3073` errors. That is normal — it is the post-build deploy step failing outside tModLoader. **0 `CS####` errors means the code compiled.** Check for `error CS` specifically.
- **Multiplayer is non-negotiable.** Any per-world state must sync. `levelCap` is computed on the server only and reaches clients via `ModSystem.NetSend` (join) and a `ModPacket` (change).
- **Localization.** No player-visible string literals in code. Use `.hjson` keys. Values starting with `{` or `[` must be quoted in hjson.
- **Comment budget.** Keep comments under ~15% of lines, and no single comment over two lines. Comment only a non-obvious invariant or a tModLoader/vanilla quirk. The code blocks in this plan are already at shipping density — transcribe them as written, do not add explanatory comments.
- **Plain C#.** Explicit `if`/`else`, named locals, no LINQ chains, no pattern-match binds, no nested ternaries.
- **Hardcoded design values.** `BaseLevel` and `MaxLevel` are `const` on `WorldManager`, never config. Per CLAUDE.md, scaling constants are game design values.
- **House packet convention.** Packet type enums live in `Common/Network/<X>PacketType.cs` as `internal enum <X>PacketType : byte` with explicit `= 0` values, dispatched from the Mod class's `HandlePacket` switch with a `default:` that logs a warning.

---

### Task 1: Boss roster from Boss Checklist

Pulls the deduplicated boss list once at load and exposes the downed count. Self-contained — nothing references it yet, so it compiles and can be reviewed on its own.

**Files:**
- Modify: `build.txt`
- Create: `Common/Systems/BossRoster.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `ARPGEnemySystem.Common.Systems.BossEntry` — `public sealed class` with fields `string Key`, `Func<bool> Downed`, `bool Excluded`.
  - `ARPGEnemySystem.Common.Systems.BossRoster` — `public class BossRoster : ModSystem` with `public static IReadOnlyList<BossEntry> Entries`, `public static int Count`, `public static bool IsDowned(BossEntry entry)`, `public static int DownedCount()`.

- [ ] **Step 1: Declare Boss Checklist as a hard requirement**

`build.txt` currently has no `modReferences` line. Replace the whole file with:

```
displayName = ARPG Enemy System
author = Shinx
version = 0.9.3.0
modReferences = BossChecklist
```

This makes the tModLoader mod browser resolve and install Boss Checklist alongside this mod. No `.csproj` `Reference` is needed — the integration is `Mod.Call` returning `object`, so there is no compile-time dependency.

- [ ] **Step 2: Create the roster system**

Create `Common/Systems/BossRoster.cs`:

```csharp
using System;
using System.Collections.Generic;
using Terraria.ModLoader;

namespace ARPGEnemySystem.Common.Systems
{
    public sealed class BossEntry
    {
        public string Key;
        public Func<bool> Downed;
        public bool Excluded;
    }

    public class BossRoster : ModSystem
    {
        private const string BossChecklistApiVersion = "1.6";

        private static readonly List<BossEntry> entries = new List<BossEntry>();

        public static IReadOnlyList<BossEntry> Entries => entries;
        public static int Count => entries.Count;

        public override void PostAddRecipes()
        {
            entries.Clear();

            if (!ModLoader.TryGetMod("BossChecklist", out Mod bossChecklist))
                throw new Exception("ARPG Enemy System requires Boss Checklist to be installed and enabled.");

            var roster = bossChecklist.Call("GetBossInfoDictionary", Mod, BossChecklistApiVersion)
                as Dictionary<string, Dictionary<string, object>>;

            if (roster == null)
                throw new Exception($"Boss Checklist returned no roster for API version {BossChecklistApiVersion}.");

            foreach (var boss in roster)
            {
                Dictionary<string, object> info = boss.Value;

                if (!info.TryGetValue("isBoss", out object isBoss) || !(bool)isBoss)
                    continue;
                if (!info.TryGetValue("downed", out object downed))
                    continue;

                entries.Add(new BossEntry { Key = boss.Key, Downed = (Func<bool>)downed });
            }

            Mod.Logger.Info($"World level roster: {entries.Count} bosses.");
        }

        // Third-party delegates are polled every second; a mod that throws is dropped
        // rather than allowed to break the count for every other entry.
        public static bool IsDowned(BossEntry entry)
        {
            if (entry.Excluded)
                return false;

            try
            {
                return entry.Downed();
            }
            catch (Exception e)
            {
                entry.Excluded = true;
                ModContent.GetInstance<ARPGEnemySystem>().Logger.Warn($"Boss Checklist entry '{entry.Key}' threw; excluded from the world level.", e);
                return false;
            }
        }

        public static int DownedCount()
        {
            int downed = 0;

            foreach (BossEntry entry in entries)
            {
                if (IsDowned(entry))
                    downed++;
            }

            return downed;
        }
    }
}
```

Notes for the implementer:

- `PostAddRecipes` is the hook Boss Checklist's own documentation specifies for this call. Do not move it to `Load` or `PostSetupContent` — the roster is not complete before recipes are added.
- Only `isBoss` and `downed` are read. The `progression` field exists on each entry and is deliberately unused.
- `IsDowned` is the single place a Boss Checklist delegate is invoked. Every consumer goes through it.

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build`
Expected: no `error CS` lines. `TML003` and `MSB3073` at the end are expected and fine.

```bash
dotnet build 2>&1 | grep -c "error CS"
```

Expected: `0`

- [ ] **Step 4: Commit**

```bash
git add build.txt Common/Systems/BossRoster.cs
git commit -m "feat(level): boss roster from Boss Checklist"
```

---

### Task 2: Replace the level cap model

Swaps kill-event registration for downed-state polling. This is one atomic task because removing `downedBossIDs` breaks every one of its readers at once — `BossManager.OnKill`, `BossFlagSync`, `CheckLevelCapCommand` and `ResetWorldCommand` cannot compile against a half-removed field, so they change together or not at all.

**Files:**
- Create: `Common/Network/EnemyPacketType.cs`
- Rewrite: `Common/Systems/WorldManager.cs`
- Modify: `ARPGEnemySystem.cs`
- Modify: `Common/GlobalNPCs/BossManager.cs:71` and `:106-109`
- Delete: `Common/GlobalNPCs/BossFlagSync.cs`
- Modify: `Common/Configs/Config.cs:17-18`
- Modify: `Localization/en-US_Mods.ARPGEnemySystem.hjson`
- Rewrite: `Common/Commands/CheckLevelCapCommand.cs`
- Rewrite: `Common/Commands/SetLevelCapCommand.cs`
- Delete: `Common/Commands/ResetWorldCommand .cs` (note the space before `.cs` in the real filename)

**Interfaces:**
- Consumes: `BossRoster.Count`, `BossRoster.DownedCount()`, `BossRoster.Entries`, `BossRoster.IsDowned(BossEntry)` from Task 1.
- Produces:
  - `WorldManager.BaseLevel` / `WorldManager.MaxLevel` — `public const int`, 10 and 200.
  - `WorldManager.levelCap` — `public static int`, unchanged name so `NPCManager` and `BossManager` keep working.
  - `WorldManager.levelCapOverride` — `public static int`, `-1` means follow boss progression.
  - `WorldManager.LevelsPerBoss()` — `public static float`.
  - `WorldManager.Recompute(bool announce)` — `public static void`.
  - `WorldManager.SendLevelCap()` — `public static void`.
  - `EnemyPacketType` — `internal enum : byte` with `LevelCap = 0`.

- [ ] **Step 1: Add the packet type**

Create `Common/Network/EnemyPacketType.cs`:

```csharp
namespace ARPGEnemySystem.Common.Network
{
    internal enum EnemyPacketType : byte
    {
        LevelCap = 0,
    }
}
```

- [ ] **Step 2: Rewrite WorldManager**

Replace the entire contents of `Common/Systems/WorldManager.cs`:

```csharp
using ARPGEnemySystem.Common.Network;
using Microsoft.Xna.Framework;
using System;
using System.IO;
using Terraria;
using Terraria.Chat;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace ARPGEnemySystem.Common.Systems
{
    public class WorldManager : ModSystem
    {
        public const int BaseLevel = 10;
        public const int MaxLevel  = 200;

        public static int levelCap = BaseLevel;

        // -1 = follow boss progression; >= 0 = /setlevelcap is holding it.
        public static int levelCapOverride = -1;

        // Hardcoded — these are game design values, not server-tuning knobs.
        // Phase 0 = pre-hardmode, 1 = post-WoF, 2 = post-all-mechs, 3 = post-Plantera.
        public static readonly float[] PhaseRates    = { 0.003f, 0.006f, 0.010f, 0.015f };
        public static readonly float[] DefPhaseRates = { 0.004f, 0.008f, 0.013f, 0.020f };

        private const int RecomputeInterval = 60;
        private int ticksUntilRecompute;

        public static int GetScalingPhase()
        {
            if (NPC.downedPlantBoss)                                               return 3;
            if (NPC.downedMechBoss1 && NPC.downedMechBoss2 && NPC.downedMechBoss3) return 2;
            if (Main.hardMode)                                                     return 1;
            return 0;
        }

        public static float LevelsPerBoss()
        {
            int bossCount = BossRoster.Count;
            if (bossCount == 0)
                return 0f;

            return (float)(MaxLevel - BaseLevel) / bossCount;
        }

        public static void Recompute(bool announce)
        {
            if (levelCapOverride >= 0)
                return;

            int newCap = BaseLevel + (int)MathF.Round(LevelsPerBoss() * BossRoster.DownedCount());
            if (newCap == levelCap)
                return;

            bool rose = newCap > levelCap;
            levelCap = newCap;
            SendLevelCap();

            if (announce && rose)
                ChatHelper.BroadcastChatMessage(NetworkText.FromKey("Mods.ARPGEnemySystem.BossKilledMessage"), Color.DarkRed);
        }

        public static void SendLevelCap()
        {
            if (Main.netMode != NetmodeID.Server)
                return;

            ModPacket packet = ModContent.GetInstance<ARPGEnemySystem>().GetPacket();
            packet.Write((byte)EnemyPacketType.LevelCap);
            packet.Write(levelCap);
            packet.Send();
        }

        public override void ClearWorld()
        {
            levelCap = BaseLevel;
            levelCapOverride = -1;
        }

        public override void PostWorldLoad()
        {
            Recompute(announce: false);
        }

        public override void PostUpdateWorld()
        {
            ticksUntilRecompute--;
            if (ticksUntilRecompute > 0)
                return;

            ticksUntilRecompute = RecomputeInterval;
            Recompute(announce: true);
        }

        public override void NetSend(BinaryWriter writer)
        {
            writer.Write(levelCap);
        }

        public override void NetReceive(BinaryReader reader)
        {
            levelCap = reader.ReadInt32();
        }
    }
}
```

What is gone from this file and why it is safe:

- `downedBossIDs`, `downedBossNum` — replaced by polling Boss Checklist. Nothing else needs the list.
- `DownedBoss()`, `RegisterBoss()`, `SyncDownedFlags()` — kill registration is gone entirely.
- `SaveWorldData()`, `LoadWorldData()` — `levelCap` was already derived, never persisted. The stale `downedBossIDs` tag in existing world files simply stops being read; tModLoader does not require every tag to be consumed, so no migration is needed.
- `DefPhaseRates[1]` changes from `0.08f` to `0.008f`. The old value was 20× phase 0 and 6× phase 2, spiking defense scaling after Wall of Flesh and then dropping it after the mechs. This is the typo fix.
- `GetScalingPhase()` loses its `downedBossIDs.Contains(NPCID.Plantera)` branch and reads `NPC.downedPlantBoss` alone, which was already the fallback in the same expression.

`PostWorldLoad` and `PostUpdateWorld` are both documented as running in single player or on the server only, so the server is authoritative with no `netMode` check needed. `ticksUntilRecompute` starts at 0, so the first `PostUpdateWorld` tick recomputes immediately and then settles into the 60-tick cadence.

- [ ] **Step 3: Add the packet handler to the Mod class**

Replace the entire contents of `ARPGEnemySystem.cs`. The file uses tabs for indentation — keep them.

```csharp
using ARPGEnemySystem.Common.Network;
using ARPGEnemySystem.Common.Systems;
using System;
using System.IO;
using Terraria.ModLoader;

namespace ARPGEnemySystem
{
	public class ARPGEnemySystem : Mod
	{
		// ARPG Item System is a mutual hard requirement, but it cannot be declared via
		// build.txt modReferences (would create a load-order cycle with ItemSystem's
		// modReferences = ARPGEnemySystem). Enforced here instead — runs after every
		// mod's Load() so HasMod sees the final loaded set.
		public override void PostSetupContent()
		{
			if (!ModLoader.HasMod("ARPGItemSystem"))
				throw new Exception("ARPG Enemy System requires ARPG Item System to be installed and enabled.");
		}

		public override void HandlePacket(BinaryReader reader, int whoAmI)
		{
			EnemyPacketType type = (EnemyPacketType)reader.ReadByte();
			switch (type)
			{
				case EnemyPacketType.LevelCap:
					WorldManager.levelCap = reader.ReadInt32();
					break;
				default:
					Logger.Warn($"Unknown packet type {type}");
					break;
			}
		}
	}
}
```

- [ ] **Step 4: Drop kill registration from BossManager**

In `Common/GlobalNPCs/BossManager.cs`, delete the whole `OnKill` override (currently lines 106–109):

```csharp
        public override void OnKill(NPC npc)
        {
            WorldManager.DownedBoss(npc);
        }

```

Then on line 71, change:

```csharp
                bool postPlantera = WorldManager.downedBossIDs.Contains(NPCID.Plantera);
```

to:

```csharp
                bool postPlantera = NPC.downedPlantBoss;
```

Leave the `using Terraria.ID;` directive in place — `NetmodeID` is still used in this file.

- [ ] **Step 5: Delete the boss flag safety net**

```bash
git rm Common/GlobalNPCs/BossFlagSync.cs
```

This `GlobalNPC` ran on every NPC death solely to call `SyncDownedFlags` and catch the Eater of Worlds case, where the last dying segment has `npc.boss == false`. Vanilla sets `downedBoss2` itself inside `NPC.DropEoWLoot()`, and Boss Checklist's `downed` predicate reads that flag, so the safety net has nothing left to do.

- [ ] **Step 6: Remove the config knob**

In `Common/Configs/Config.cs`, delete these lines (currently 17–18) along with the blank line after them:

```csharp
        [DefaultValue(10)]
        public int LevelCapIncreasePerBossDowned;
```

`[Header("Scaling")]` and everything below it stays.

Then in `Localization/en-US_Mods.ARPGEnemySystem.hjson`, delete this block and the blank line that follows it (the file is tab-indented):

```
		LevelCapIncreasePerBossDowned: {
			Tooltip: ""
			Label: Level Cap Increase Per Boss Downed
		}
```

- [ ] **Step 7: Rewrite /checklevelcap**

Replace the entire contents of `Common/Commands/CheckLevelCapCommand.cs`:

```csharp
using ARPGEnemySystem.Common.Systems;
using Terraria.ModLoader;

namespace ARPGEnemySystem.Common.Commands
{
    public class CheckLevelCapCommand : ModCommand
    {
        public override CommandType Type
            => CommandType.World;
        public override string Command
            => "checklevelcap";
        public override string Description
            => "Report the world level and the boss roster it is derived from";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            int downed = BossRoster.DownedCount();
            caller.Reply($"World level {WorldManager.levelCap} — {downed}/{BossRoster.Count} bosses downed, {WorldManager.LevelsPerBoss():0.##} levels each.");

            if (WorldManager.levelCapOverride >= 0)
                caller.Reply($"Held at {WorldManager.levelCapOverride} by /setlevelcap. Use /setlevelcap auto to release.");

            foreach (BossEntry entry in BossRoster.Entries)
            {
                if (BossRoster.IsDowned(entry))
                    caller.Reply(entry.Key);
            }
        }
    }
}
```

`CommandType.World` is documented as "Command can be used in Chat in SP and MP, but executes on the Server in MP", so this reads server-authoritative state. `caller.Reply` still reaches the calling player. These strings are debug output and stay as literals — they are not player-facing content.

- [ ] **Step 8: Rewrite /setlevelcap**

Replace the entire contents of `Common/Commands/SetLevelCapCommand.cs`:

```csharp
using ARPGEnemySystem.Common.Systems;
using Terraria.ModLoader;

namespace ARPGEnemySystem.Common.Commands
{
    public class SetLevelCapCommand : ModCommand
    {
        public override CommandType Type
            => CommandType.World;
        public override string Command
            => "setlevelcap";
        public override string Description
            => "Hold the world level for testing. Usage: /setlevelcap <value> | auto";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (args.Length > 0 && args[0] == "auto")
            {
                WorldManager.levelCapOverride = -1;
                WorldManager.Recompute(announce: false);
                caller.Reply($"World level follows boss progression again ({WorldManager.levelCap}).");
                return;
            }

            if (args.Length == 0 || !int.TryParse(args[0], out int value) || value < 0)
            {
                caller.Reply("Usage: /setlevelcap <value> | auto");
                return;
            }

            WorldManager.levelCapOverride = value;
            WorldManager.levelCap = value;
            WorldManager.SendLevelCap();
            caller.Reply($"World level held at {value}. New enemies will spawn at level ~{value}.");
        }
    }
}
```

The override is what stops the 60-tick recompute from stomping the value a second later. Note the ordering in the `auto` branch: clear the override *first*, because `Recompute` returns early while it is set.

- [ ] **Step 9: Delete /resetworld**

```bash
git rm "Common/Commands/ResetWorldCommand .cs"
```

The filename genuinely contains a space before `.cs` — quote it. The command cleared `downedBossIDs` and `levelCap`, neither of which exists now. Boss progression lives in vanilla's `NPC.downed*` flags and each content mod's own save data, which this mod does not own and must not clear. `/setlevelcap` covers the testing need.

- [ ] **Step 10: Verify it compiles**

Run: `dotnet build`

```bash
dotnet build 2>&1 | grep "error CS"
```

Expected: no output. If there is any, the most likely cause is a missed reader of a deleted `WorldManager` member — grep for it:

```bash
grep -rn "downedBossIDs\|downedBossNum\|DownedBoss\|SyncDownedFlags\|LevelCapIncreasePerBossDowned" --include="*.cs" . | grep -v "/bin/" | grep -v "/obj/"
```

Expected: no output.

- [ ] **Step 11: Commit**

```bash
git add -A
git commit -m "feat(level): world level from Boss Checklist roster

Replaces count-based levelCap with a fixed 10-200 range over the
deduplicated roster. Fixes Twins/Moon Lord double-counting and the
DefPhaseRates[1] typo."
```

---

### Task 3: Update CLAUDE.md

`CLAUDE.md` documents the system this change replaces, and its scaling table was already stale before this work. A reviewer reads prose differently from code, so this is its own task.

**Files:**
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: the final shape of `WorldManager` and `BossRoster` from Tasks 1–2.
- Produces: nothing code depends on.

- [ ] **Step 1: Fix the debug command list**

Under `## In-game Debug Commands`, replace:

```markdown
- `/resetworld` — resets the world's `levelCap` and boss progression tracking to zero
- `/setlevelcap <value>` — sets `WorldManager.levelCap` directly for testing high-level enemy spawns
```

with:

```markdown
- `/checklevelcap` — reports the world level, `downed/N` from the boss roster, the per-boss value, and which entries are downed
- `/setlevelcap <value>` — holds `WorldManager.levelCap` for testing; `/setlevelcap auto` releases it back to boss progression
```

- [ ] **Step 2: Record the Boss Checklist requirement**

Under `## Cross-Mod Dependency`, after the existing ARPGItemSystem paragraphs, add:

```markdown
**Boss Checklist** is a hard requirement declared the normal way — `modReferences = BossChecklist` in `build.txt`. No load-order cycle exists, so no runtime `HasMod` check is needed. It is the sole source of the boss roster that drives world level: `BossRoster` calls `GetBossInfoDictionary` (API version `1.6`) in `PostAddRecipes` and keeps each `isBoss` entry's `downed` predicate. No `.csproj` `Reference` is needed because the integration is `Mod.Call` returning `object`.
```

- [ ] **Step 3: Rewrite the High-Level Concept sentence**

Replace:

```markdown
Bosses receive a level only. The level cap grows with boss progression and is persisted per-world.
```

with:

```markdown
Bosses receive a level only. The level cap runs from 10 on a fresh world to 200 when every boss in the loaded mod set is dead, and is derived at runtime rather than persisted.
```

- [ ] **Step 4: Replace the world level section of Core Data Flow**

Replace the first two lines of the data flow block:

```
WorldManager.levelCap
       ↓ (consumed by)
```

with:

```
BossRoster (Boss Checklist roster + downed predicates)
       ↓ (polled once per second, server only)
WorldManager.levelCap = 10 + (190 / N) × downed
       ↓ (consumed by)
```

- [ ] **Step 5: Correct the stale scaling constants table**

Replace:

```markdown
| `PhaseRates`    | `{0.003, 0.007, 0.015, 0.030}` | HP/damage scaling per phase         |
| `DefPhaseRates` | `{0.004, 0.010, 0.020, 0.040}` | Defense scaling per phase (steeper) |
```

with:

```markdown
| `PhaseRates`    | `{0.003, 0.006, 0.010, 0.015}` | HP/damage scaling per phase         |
| `DefPhaseRates` | `{0.004, 0.008, 0.013, 0.020}` | Defense scaling per phase (steeper) |
| `BaseLevel`     | `10`                           | World level on a fresh world        |
| `MaxLevel`      | `200`                          | World level with every boss downed  |
```

- [ ] **Step 6: Recompute the reference values**

The existing reference multipliers were calculated from the stale rates. Replace:

```markdown
- Level 50, phase 0 (pre-HM): 1.33× (+33%)
- Level 50, phase 1 (post-WoF): 1.76× (+76%)
- Level 100, phase 2 (post-mechs): 4.77× (+377%)
- Level 150, phase 3 (post-Plantera): 12.91× (+1191%)
```

with:

```markdown
- Level 50, phase 0 (pre-HM): 1.26× (+26%)
- Level 50, phase 1 (post-WoF): 1.52× (+52%)
- Level 100, phase 2 (post-mechs): 2.91× (+191%)
- Level 150, phase 3 (post-Plantera): 5.54× (+454%)
- Level 200, phase 3 (everything downed): 7.29× (+629%)
```

These are `1 + level^1.14 × PhaseRates[phase]` at the default `ScalingExponent`. Level 200 is the tuning target — the point the curve is balanced against.

- [ ] **Step 7: Correct the stale server config defaults**

The server config table has drifted from `Config.cs` on two scaling values. Replace:

```markdown
| `ScalingExponent`    | `1.13`  | HP/damage curve shape                              |
| `DefScalingExponent` | `1.15`  | Defense curve shape (steeper than ScalingExponent) |
| `DefenseFloor`       | `0.10`  | Additive min defense = level × floor               |
```

with:

```markdown
| `ScalingExponent`    | `1.14`  | HP/damage curve shape                              |
| `DefScalingExponent` | `1.15`  | Defense curve shape (steeper than ScalingExponent) |
| `DefenseFloor`       | `0.70`  | Additive min defense = level × floor               |
```

In the `Config.cs` bullet under `### Key Files`, the prose also says `ScalingExponent` has "default 1.3". Change that to "default 1.14".

**Do not fix the rest of that bullet.** It also lists `ElementalResistanceCap`, `EnemyElementalChance`, `EnemyBaseElementalAllocationPct`, `PlayerPhysResCap` and `EnemyElemResPerLevel` as server config fields, and none of them are in `Config.cs` any more — the caps moved to constants on `ElementalMath`. That drift is real but it is elemental-system documentation, not scaling, and it is outside this change. Report it at the end of the task so it can be scoped separately.

- [ ] **Step 8: Fix the Phase 3 predicate**

Replace:

```markdown
- Phase 3 — post-Plantera (`downedBossIDs.Contains(NPCID.Plantera) || NPC.downedPlantBoss`)
```

with:

```markdown
- Phase 3 — post-Plantera (`NPC.downedPlantBoss`)
```

Then add this paragraph after the Phase System bullet list:

```markdown
`GetScalingPhase()` tops out at 3, so every post-Moon-Lord modded boss fight uses `PhaseRates[3]`. This is a known limitation, deliberately left out of the world-level work.
```

- [ ] **Step 9: Rewrite the Key Files entries**

Replace the `WorldManager.cs` bullet in `### Key Files` with:

```markdown
- **`Common/Systems/BossRoster.cs`** — Pulls every `isBoss` entry from Boss Checklist in `PostAddRecipes` and keeps its `downed` predicate. `IsDowned(entry)` is the only place a third-party delegate is invoked; an entry whose predicate throws is logged and excluded for the session. `progression` is available on each entry but deliberately unused.
- **`Common/Systems/WorldManager.cs`** — Owns `levelCap`, the hardcoded `PhaseRates`/`DefPhaseRates`/`BaseLevel`/`MaxLevel`, and `GetScalingPhase()`. `Recompute()` sets `levelCap = BaseLevel + LevelsPerBoss() × BossRoster.DownedCount()`, called from `PostWorldLoad` and every 60 ticks in `PostUpdateWorld` — both server/single-player only, so the server is authoritative without a `netMode` check. Nothing is persisted: `levelCap` is a pure function of the roster and the downed predicates. `levelCapOverride` (−1 = off) lets `/setlevelcap` hold a value against the recompute.
```

Delete the `BossFlagSync.cs` bullet entirely.

In the `BossManager.cs` bullet, replace `Calls `WorldManager.DownedBoss()` on kill.` with `No longer registers kills — world level polls downed state instead.`

In the `Config.cs` bullet, delete `LevelCapIncreasePerBossDowned`, from the list of server-side fields.

- [ ] **Step 10: Update the Multiplayer Sync section**

At the end of `### Multiplayer Sync`, add:

```markdown
`WorldManager.NetSend`/`NetReceive` send `levelCap` as one int to joining clients, and `SendLevelCap()` broadcasts an `EnemyPacketType.LevelCap` packet whenever the recompute changes it. Clients never compute `levelCap` — `NPCManager.SetDefaults` and `BossManager.OnSpawn` are both guarded by `Main.netMode != NetmodeID.MultiplayerClient` and NPC levels arrive through `SendExtraAI`.
```

- [ ] **Step 11: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: world level from boss roster; correct stale phase rates"
```

---

### Task 4: In-game verification

The only real gate. A clean build proves nothing about behaviour here. Work through this in tModLoader and report actual observed numbers, not expectations.

**Files:** none.

**Interfaces:**
- Consumes: everything from Tasks 1–3.
- Produces: the observed value of `N`, which is what the scaling curve gets tuned against.

- [ ] **Step 1: Build and load**

Build in tModLoader via Workshop → Mod Sources → `ARPGEnemySystem` → Build & Reload. Confirm Boss Checklist is enabled. Check `client.log` for the roster line:

```
World level roster: <N> bosses.
```

- [ ] **Step 2: Confirm the roster size on a fresh vanilla world**

Create a fresh world with only the three ARPG mods and Boss Checklist enabled. Run `/checklevelcap`.

Expected: `World level 10 — 0/18 bosses downed, 10.56 levels each.`

`N = 18` is the expectation, not a verified fact — Boss Checklist's `isBoss` flag could not be confirmed from source for Betsy, Ogre, Dark Mage or Martian Saucer. **A different `N` is not a failure.** Record the actual number and the entry list; if it is not 18, report which unexpected entries appear so `MaxLevel` can be re-judged before tuning.

- [ ] **Step 3: Confirm the double-counting fix**

The whole point of the change. With `/setlevelcap auto` active:

1. Note `levelCap`, summon and kill **The Twins**, wait two seconds, run `/checklevelcap`. The downed count must rise by **1**, not 2, and `levelCap` by one boss's worth.
2. Note `levelCap`, kill **Moon Lord**, wait two seconds, run `/checklevelcap`. The downed count must rise by **1**, not 3.
3. Kill the **Eater of Worlds**. It must register despite the last segment having `npc.boss == false` — this is the case `BossFlagSync` used to cover.

- [ ] **Step 4: Confirm there is no stale-level window**

On a world with several bosses already downed, exit to the menu and re-enter. Run `/checklevelcap` immediately. `levelCap` must already be correct — `PostWorldLoad` computes it before players can enter, so it must never read 10 and then jump a second later. Kill an enemy during the first second and confirm its level matches the world level.

- [ ] **Step 5: Confirm the modded roster**

Enable a content mod that registers bosses with Boss Checklist. Load a world and run `/checklevelcap`. `N` must grow and the per-boss value must shrink, with `levelCap` still 10 on a fresh world.

- [ ] **Step 6: Confirm multiplayer**

Host a server with at least one client.

1. The joining client's `/checklevelcap` reports the same `levelCap` as the host.
2. Kill a boss; the client's `levelCap` updates within a second.
3. The `BossKilledMessage` announcement appears **in chat for both players**, not only in the server console. This is the `Main.NewText` → `ChatHelper.BroadcastChatMessage` fix.

- [ ] **Step 7: Confirm the override**

Run `/setlevelcap 150`, wait at least three seconds, then `/checklevelcap`. It must still report 150 and say it is held. Run `/setlevelcap auto` and confirm it returns to the computed value.

- [ ] **Step 8: Sanity-check the defense curve fix**

Compare a post-Wall-of-Flesh enemy's defense against a post-mechs enemy of a similar level using the enemy stat panel (`ConfigClient.EnableEnemyStatPanel`). Defense scaling must increase across phases. Before the `DefPhaseRates[1]` fix it spiked after Wall of Flesh and dropped after the mechs.
