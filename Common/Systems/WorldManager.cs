using ARPGEnemySystem.Common.Configs;
using Microsoft.Xna.Framework;
using Terraria.Localization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace ARPGEnemySystem.Common.Systems
{
    public class WorldManager : ModSystem
    {
        public static List<int> downedBossIDs = new List<int>();
        public static int downedBossNum = 0;
        public static int levelCap = 0;

        // Hardcoded — these are game design values, not server-tuning knobs.
        // Phase 0 = pre-hardmode, 1 = post-WoF, 2 = post-all-mechs, 3 = post-Plantera.
        public static readonly float[] PhaseRates    = { 0.003f, 0.007f, 0.015f, 0.030f };
        public static readonly float[] DefPhaseRates = { 0.004f, 0.010f, 0.020f, 0.040f };
        // DefScalingExponent and DefenseFloor are server config — see Config.cs.

        public static int GetScalingPhase()
        {
            if (downedBossIDs.Contains(NPCID.Plantera) || NPC.downedPlantBoss)        return 3;
            if (NPC.downedMechBoss1 && NPC.downedMechBoss2 && NPC.downedMechBoss3)    return 2;
            if (Main.hardMode)                                                         return 1;
            return 0;
        }

        public override void ClearWorld()
        {
            downedBossIDs = new List<int>();
            downedBossNum = 0;
            levelCap = 0;
        }

        // Primary kill path — called by BossManager.OnKill for any NPC with npc.boss == true.
        // Handles all vanilla single-NPC bosses and all modded bosses.
        // For vanilla multi-segment bosses (EoW, Destroyer) where the last dying segment may
        // have npc.boss == false, BossFlagSync.OnKill calls SyncDownedFlags as a safety net.
        public static void DownedBoss(NPC npc)
        {
            if (npc.boss)
                RegisterBoss(npc.type, announce: true);
        }

        // Walks every vanilla NPC.downed* flag and registers any boss whose flag is set
        // but whose NPC ID is not yet in downedBossIDs. Called:
        //   - From BossFlagSync.OnKill (announce: true) — catches segment-death edge cases.
        //   - From LoadWorldData (announce: false) — backfills kills missed in prior sessions.
        public static void SyncDownedFlags(bool announce)
        {
            if (NPC.downedSlimeKing)      RegisterBoss(NPCID.KingSlime,       announce);
            if (NPC.downedBoss1)          RegisterBoss(NPCID.EyeofCthulhu,    announce);
            if (NPC.downedBoss2)          RegisterBoss(WorldGen.crimson ? NPCID.BrainofCthulhu : NPCID.EaterofWorldsHead, announce);
            if (NPC.downedBoss3)          RegisterBoss(NPCID.SkeletronHead,   announce);
            if (NPC.downedQueenBee)       RegisterBoss(NPCID.QueenBee,        announce);
            if (NPC.downedDeerclops)      RegisterBoss(NPCID.Deerclops,       announce);
            if (Main.hardMode)            RegisterBoss(NPCID.WallofFlesh,     announce);
            if (NPC.downedQueenSlime)     RegisterBoss(NPCID.QueenSlimeBoss,  announce);
            if (NPC.downedMechBoss1)      RegisterBoss(NPCID.TheDestroyer,    announce);
            if (NPC.downedMechBoss2)      RegisterBoss(NPCID.Retinazer,       announce);
            if (NPC.downedMechBoss3)      RegisterBoss(NPCID.SkeletronPrime,  announce);
            if (NPC.downedPlantBoss)      RegisterBoss(NPCID.Plantera,        announce);
            if (NPC.downedGolemBoss)      RegisterBoss(NPCID.Golem,           announce);
            if (NPC.downedFishron)        RegisterBoss(NPCID.DukeFishron,     announce);
            if (NPC.downedEmpressOfLight) RegisterBoss(NPCID.HallowBoss,      announce);
            if (NPC.downedAncientCultist) RegisterBoss(NPCID.CultistBoss,     announce);
            if (NPC.downedMoonlord)       RegisterBoss(NPCID.MoonLordCore,    announce);
        }

        private static void RegisterBoss(int npcType, bool announce)
        {
            if (downedBossIDs.Any(x => x == npcType))
                return;

            downedBossNum++;
            downedBossIDs.Add(npcType);
            levelCap += ModContent.GetInstance<Config>().LevelCapIncreasePerBossDowned;

            if (announce)
                Main.NewText(Language.GetTextValue("Mods.ARPGEnemySystem.BossKilledMessage"), Color.DarkRed);
        }

        public override void SaveWorldData(TagCompound tag)
        {
            tag["downedBossIDs"] = downedBossIDs;
            tag["downedBossNum"] = downedBossNum;
        }

        public override void LoadWorldData(TagCompound tag)
        {
            if (tag.ContainsKey("downedBossIDs"))
                downedBossIDs = (List<int>)tag.GetList<int>("downedBossIDs");
            if (tag.ContainsKey("downedBossNum"))
                downedBossNum = tag.GetAsInt("downedBossNum");

            // Backfill from vanilla flags — catches vanilla multi-segment bosses (EoW, Destroyer)
            // where the segment-death edge case prevented BossManager.OnKill from firing.
            SyncDownedFlags(announce: false);

            levelCap = downedBossIDs.Count * ModContent.GetInstance<Config>().LevelCapIncreasePerBossDowned;
        }

        public override void NetSend(BinaryWriter writer)
        {
            writer.Write(downedBossIDs.Count);
            foreach (var id in downedBossIDs)
            {
                writer.Write(id);
            }
        }

        public override void NetReceive(BinaryReader reader)
        {
            downedBossIDs.Clear();
            var idListCount = reader.ReadInt32();
            for (int i = 0; i < idListCount; i++)
            {
                downedBossIDs.Add(reader.ReadInt32());
            }
            levelCap = downedBossIDs.Count * ModContent.GetInstance<Config>().LevelCapIncreasePerBossDowned;
        }
    }
}
