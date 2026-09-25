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

        // NPC types of every boss and miniboss entry; BossPlayerScaling gives these full per-player life.
        private static readonly HashSet<int> playerScaledTypes = new HashSet<int>();

        public static IReadOnlyList<BossEntry> Entries => entries;
        public static int Count => entries.Count;

        public static bool ScalesWithPlayers(int npcType) => playerScaledTypes.Contains(npcType);

        public override void PostAddRecipes()
        {
            entries.Clear();
            playerScaledTypes.Clear();

            if (!ModLoader.TryGetMod("BossChecklist", out Mod bossChecklist))
                throw new Exception("ARPG Enemy System requires Boss Checklist to be installed and enabled.");

            var roster = bossChecklist.Call("GetBossInfoDictionary", Mod, BossChecklistApiVersion)
                as Dictionary<string, Dictionary<string, object>>;

            if (roster == null)
                throw new Exception($"Boss Checklist returned no roster for API version {BossChecklistApiVersion}.");

            foreach (var boss in roster)
            {
                Dictionary<string, object> info = boss.Value;

                bool isBoss = info.TryGetValue("isBoss", out object bossFlag) && (bool)bossFlag;
                bool isMiniboss = info.TryGetValue("isMiniboss", out object minibossFlag) && (bool)minibossFlag;

                if ((isBoss || isMiniboss) && info.TryGetValue("npcIDs", out object npcIDs) && npcIDs is List<int> types)
                    playerScaledTypes.UnionWith(types);

                if (!isBoss)
                    continue;
                if (!info.TryGetValue("downed", out object downed))
                    continue;

                entries.Add(new BossEntry { Key = boss.Key, Downed = (Func<bool>)downed });
            }

            Mod.Logger.Info($"World level roster: {entries.Count} bosses.");
            Mod.Logger.Info($"Player-scaled boss NPC types: {playerScaledTypes.Count}.");
        }

        public override void Unload()
        {
            entries.Clear();
            playerScaledTypes.Clear();
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
