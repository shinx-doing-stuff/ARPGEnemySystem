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
