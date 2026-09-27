using ARPGEnemySystem.Common.Network;
using ARPGEnemySystem.Common.Scaling;
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
        public const int BaseLevel = ScalingMath.BaseLevel;
        public const int MaxLevel  = ScalingMath.MaxLevel;

        public static int levelCap = BaseLevel;

        // -1 = follow boss progression; >= 0 = /setlevelcap is holding it.
        public static int levelCapOverride = -1;

        // Hardcoded — these are game design values, not server-tuning knobs.
        // Phase 0 = pre-hardmode, 1 = post-WoF, 2 = post-all-mechs, 3 = post-Plantera.
        public static readonly float[] PhaseRates    = ScalingMath.PhaseRates;
        public static readonly float[] DefPhaseRates = ScalingMath.DefPhaseRates;

        private const int RecomputeInterval = 60;
        private int ticksUntilRecompute;

        public static int GetScalingPhase() => ScalingMath.GetScalingPhase();

        public static float LevelsPerBoss() => ScalingMath.LevelsPerBoss(BossRoster.Count);

        public static void Recompute(bool announce)
        {
            if (levelCapOverride >= 0)
                return;

            int newCap = ScalingMath.LevelCap(BossRoster.Count, BossRoster.DownedCount());
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
