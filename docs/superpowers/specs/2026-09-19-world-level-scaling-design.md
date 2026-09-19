# World Level Scaling

**Date:** 2026-09-19
**Scope:** ARPGEnemySystem only

## Goal

World level must mean the same thing regardless of how much content is installed. Today
`levelCap = downedBossIDs.Count × LevelCapIncreasePerBossDowned`, so a modpack that adds 20 bosses
triples the level a player reaches by Moon Lord, and no single set of scaling constants can balance
both cases.

Replace it with a fixed range: **level 10 on a fresh world, level 200 when every installed boss is
dead**, whatever "every installed boss" happens to mean. Level 200 becomes the single point the
scaling curve is tuned against.

## Non-goals

- Weighting bosses by difficulty or progression order. Every boss is worth the same share.
- Preserving world level across a mid-playthrough mod change. Installing a mod changes the
  denominator and the level moves; that is the player's decision to make.
- A phase beyond `GetScalingPhase() == 3`. Post-Moon-Lord bosses still use `PhaseRates[3]`.
- Player level caps. Nothing in ARPGCharacterSystem reads `levelCap`.

## Design

### Formula

```
N        = number of bosses in the loaded mod set
levelCap = BaseLevel + (MaxLevel - BaseLevel) × downed / N
```

With `BaseLevel = 10` and `MaxLevel = 200`, each boss is worth `190 / N` levels.

| Installed | N | Per boss | Fresh world | Everything cleared |
| --------- | -- | -------- | ----------- | ------------------ |
| Vanilla | 18 | 10.56 | 10 | 200 |
| + a 30-boss content mod | 48 | 3.96 | 10 | 200 |

The endpoints are fixed. Mid-game pacing varies by modpack — a player running a content mod with
a lot of post-Moon-Lord content reaches Moon Lord at a lower level than a vanilla player, because
those later bosses are in `N`. This is accepted: 200 means "cleared what you installed", not
"cleared vanilla".

Both constants are hardcoded in `WorldManager` alongside `PhaseRates`, per the CLAUDE.md
convention that scaling values are game design values rather than server config.
`Config.LevelCapIncreasePerBossDowned` is deleted.

### Roster source

Boss Checklist, as a hard requirement (`modReferences = BossChecklist` in `build.txt`). No
`.csproj` reference is needed — the integration is `Mod.Call` returning `object`.

```csharp
bossChecklist.Call("GetBossInfoDictionary", Mod, "1.6")
// → Dictionary<string, Dictionary<string, object>>
```

Two fields are used and no others: `isBoss` to filter out minibosses and events, and `downed`
(a `Func<bool>`) to test progression. The `progression` field is deliberately unused.

Boss Checklist registers vanilla bosses itself via `InitializeVanillaEntries()`, and the call
returns `bossTracker.SortedEntries`, a single list holding vanilla and modded entries. So there is
no separate vanilla path.

This is what fixes the counting bug. `BossManager.AppliesToEntity` is `entity.boss`, and
`DownedBoss(npc)` registers `npc.type`, so killing The Twins registers Retinazer *and* Spazmatism
(+20 levels) and killing Moon Lord registers Head, Hand *and* Core (+30). A full vanilla clear
reaches 220 rather than the 170 the 17-entry `SyncDownedFlags` list implies. Boss Checklist's
entries are already one-per-boss, so the vanilla double-counting this change was built to fix —
the Twins as two NPC types, Moon Lord as three — is genuinely impossible now: each is one roster
entry behind one predicate. That is not an unconditional guarantee: Eater of Worlds and Brain of
Cthulhu are two roster entries sharing a single predicate, `NPC.downedBoss2`, so killing either
still counts twice. The count is bounded by the roster rather than by how many NPCs a boss spawns,
which is the fix that matters.

### Components

**`Common/Systems/BossRoster.cs`** — new. Owns the roster and nothing else.

```csharp
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
```

`IsDowned` is the only place a third-party delegate is invoked, so one broken mod cannot throw 60
times a minute or break the count for everyone else.

**`Common/Systems/WorldManager.cs`** — keeps `levelCap`, the phase rates and `GetScalingPhase()`.
Everything to do with tracking kills is deleted (see below).

```csharp
public const int BaseLevel = 10;
public const int MaxLevel  = 200;

public static int levelCap = BaseLevel;

// -1 = follow boss progression; >= 0 = /setlevelcap is holding it.
public static int levelCapOverride = -1;

public static readonly float[] PhaseRates    = { 0.003f, 0.006f, 0.010f, 0.015f };
public static readonly float[] DefPhaseRates = { 0.004f, 0.008f, 0.013f, 0.020f };

private const int RecomputeInterval = 60;
private int ticksUntilRecompute;

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
```

`GetScalingPhase()` loses its `downedBossIDs` branch:

```csharp
if (NPC.downedPlantBoss) return 3;
```

`BossManager.cs:71` makes the same substitution for its `postPlantera` local.

### Timing

`PostWorldLoad` is documented as running after "all modded world data is loaded" and "Only called
in single player or on the server" — so the first compute sees every mod's restored downed flags,
and there is no window where enemies spawn against a stale level.

`PostUpdateWorld` is likewise server/single-player only, which makes the server authoritative
without a netMode check. The poll is `N` delegate calls once per second, each reading a static
bool; at `N = 100` that is 100 calls per second.

Polling rather than hooking `OnKill` is deliberate: a modded boss sets its own downed flag on its
own schedule, and some do it after `OnKill` returns. Polling is correct regardless of when a mod
chooses to write its flag.

### Multiplayer

`levelCap` is computed only on the server. Clients receive it:

- **On join** — `ModSystem.NetSend`/`NetReceive` write one `int`, replacing the current boss-ID list.
- **On change** — a `ModPacket` broadcast from `SendLevelCap()`.

`ARPGEnemySystem.cs` gains the message enum and handler:

```csharp
public enum ARPGMessageType : byte
{
    LevelCap
}

public override void HandlePacket(BinaryReader reader, int whoAmI)
{
    ARPGMessageType type = (ARPGMessageType)reader.ReadByte();

    switch (type)
    {
        case ARPGMessageType.LevelCap:
            WorldManager.levelCap = reader.ReadInt32();
            break;
    }
}
```

No client gameplay code reads `levelCap` today — `NPCManager.SetDefaults` and `BossManager.OnSpawn`
are both guarded by `Main.netMode != NetmodeID.MultiplayerClient`, and NPC levels reach clients
through `SendExtraAI`. The packet keeps the value correct everywhere rather than leaving a
documented caveat for the next thing that reads it.

This also fixes a live bug: the current announce is `Main.NewText` inside `RegisterBoss`, which runs
on the server, where it writes to the console and reaches no client.
`ChatHelper.BroadcastChatMessage` handles both cases in one call — `BroadcastChatMessageAs` branches
on `Main.dedServ` internally and falls through to a local `DisplayMessage` in single player.

### Commands

Both move to `CommandType.World` ("executes on the Server in MP"). `/setlevelcap` is currently
`CommandType.Chat` and so runs client-side, where it cannot affect a server at all.

| Command | Change |
| ------- | ------ |
| `/checklevelcap` | Rewritten: reports `levelCap`, `downed/N`, `WorldManager.LevelsPerBoss()`, and the keys of downed entries. Reads through `BossRoster.IsDowned` so a throwing entry cannot break it. |
| `/setlevelcap <n>` | Sets `levelCapOverride` so the recompute does not stomp it, then syncs. `/setlevelcap auto` clears the override and recomputes. |
| `/resetworld` | Deleted. It cleared `downedBossIDs` and `levelCap`, neither of which exists now; boss progression lives in vanilla's flags and each mod's own save data, which we do not own. `/setlevelcap` covers the testing need. |

### Removals

From `WorldManager`: `downedBossIDs`, `downedBossNum`, `DownedBoss()`, `SyncDownedFlags()`,
`RegisterBoss()`, `SaveWorldData()`, `LoadWorldData()`.

Whole files and members:

- `Common/GlobalNPCs/BossFlagSync.cs` — existed only to catch the Eater of Worlds segment-death
  case, where the last dying segment has `npc.boss == false`. Vanilla sets `downedBoss2` itself in
  `DropEoWLoot()`, and Boss Checklist's `downed` predicate reads that flag.
- `BossManager.OnKill` — the whole body was `WorldManager.DownedBoss(npc)`.
- `Config.LevelCapIncreasePerBossDowned`.

No save migration is needed. `levelCap` was already derived rather than persisted, and the
`downedBossIDs` tag simply stops being read; tModLoader ignores unread tags.

## Rejected alternatives

**Weighting bosses by Boss Checklist's `progression` value**, so that each vanilla boss owns a
10-level band and modded bosses subdivide the band they fall into. This holds level constant at
every vanilla milestone across modpacks, not just at the end, and lets post-Moon-Lord content push
past 200. Rejected as more machinery than the problem needs: it requires band assignment, a
derived per-band rate, a separate rule above the last vanilla anchor, and it makes level exceed the
tuning target. A fixed 10–200 range with one divisor is the simpler contract.

**A high-water clamp** persisting peak `levelCap` so a mid-save mod install cannot lower it.
Rejected: it reintroduces save state to hide a number that is not actually wrong, and a player who
changes their mod set mid-playthrough owns that outcome.

**Deriving the roster from `ContentSamples.NpcsByNetId` filtered on `npc.boss`**, which needs no
dependency. Rejected: it counts boss NPC *types*, not bosses — 22 in vanilla against 17 real ones —
and cannot know that Retinazer and Spazmatism are one boss. The distortion scales with how many
multi-segment bosses a modpack ships, so `N` would stop meaning anything comparable.

## Side fixes

Both are in files this change already touches.

`DefPhaseRates[1]` is `0.08f`, which is 20× phase 0 and 6× phase 2 — defense scaling spikes after
Wall of Flesh and then drops after the mechs. Corrected to `0.008f`.

`CLAUDE.md` documents `PhaseRates` as `{0.003, 0.007, 0.015, 0.030}` against the code's
`{0.003, 0.006, 0.010, 0.015}`, and its reference HP multipliers were computed from those stale
numbers. The world-level section is rewritten for this design and the table corrected.

## Verification

No test harness; this is an in-game checklist.

1. `/checklevelcap` on a fresh vanilla world reports **N = 18**. This is also the check on Boss
   Checklist's `isBoss` flags for the entries that could not be confirmed from source — Betsy, Ogre,
   Dark Mage, Martian Saucer. A different N is not a failure, but the number must be understood
   before tuning against it.
2. Kill The Twins — `levelCap` rises by exactly one boss's worth, not two. Kill Moon Lord — one,
   not three.
3. Kill the Eater of Worlds — it registers, with `BossFlagSync` gone.
4. Load a world with a content mod enabled — `N` grows, the per-boss value shrinks, `levelCap`
   recomputes on entry with no stale-level window.
5. Multiplayer: a joining client reads the same `levelCap`; killing a boss updates it on the client
   and the chat announcement reaches players rather than the console.
6. `/setlevelcap 150` holds through at least two seconds of recompute ticks; `/setlevelcap auto`
   restores the computed value.
