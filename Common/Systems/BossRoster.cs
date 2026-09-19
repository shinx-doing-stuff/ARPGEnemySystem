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

        public override void Unload()
        {
            entries.Clear();
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
