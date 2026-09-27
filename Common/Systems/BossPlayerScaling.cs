using ARPGEnemySystem.Common.Configs;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using Terraria;
using Terraria.Chat;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace ARPGEnemySystem.Common.Systems
{
    // Vanilla's Expert/Master boss life grows by less than one boss per extra player
    // (1.35x at 2 players, 2.63x at 4). Bosses and minibosses from the Boss Checklist
    // roster get solo life x player count instead. Normal mode, boss limbs and
    // mid-fight joins stay vanilla.
    public class BossPlayerScaling : ModSystem
    {
        // Worm bosses spawn many segments of one type at once; announce each type once.
        private const uint AnnounceCooldownTicks = 60;
        private static readonly Dictionary<int, uint> lastAnnounced = new Dictionary<int, uint>();

        public override void Load()
        {
            On_NPC.ScaleStats += ScaleStats;
        }

        public override void ClearWorld()
        {
            lastAnnounced.Clear();
        }

        private static void ScaleStats(On_NPC.orig_ScaleStats orig, NPC self, int? activePlayersCount, GameModeData gameModeData, float? strengthOverride)
        {
            if (!BossRoster.ScalesWithPlayers(self.type))
            {
                orig(self, activePlayersCount, gameModeData, strengthOverride);
                return;
            }

            // Scale as a solo fight so vanilla's and the boss mod's own difficulty tuning still apply.
            int players = activePlayersCount ?? NPC.GetActivePlayerCount();
            orig(self, 1, gameModeData, strengthOverride);

            // Vanilla only records a player count when it ran its Expert multiplayer step (never in Normal mode).
            if (self.statsAreScaledForThisManyPlayers != 1 || players <= 1)
                return;

            // Golem's AI and the NPC spawn packet read the stored count, so it must be the real one.
            self.statsAreScaledForThisManyPlayers = players;
            self.lifeMax *= players;
            self.life = self.lifeMax;
        }

        // Debug chat line, called once a boss's level scaling is applied.
        public static void Announce(NPC npc, int lifeBeforeLevel, int level)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient || !BossRoster.ScalesWithPlayers(npc.type))
                return;
            if (!ModContent.GetInstance<Config>().EnableBossScalingLog)
                return;

            if (lastAnnounced.TryGetValue(npc.type, out uint tick) && Main.GameUpdateCount - tick < AnnounceCooldownTicks)
                return;
            lastAnnounced[npc.type] = Main.GameUpdateCount;

            int players = npc.statsAreScaledForThisManyPlayers;
            NetworkText text = players >= 1
                ? NetworkText.FromKey("Mods.ARPGEnemySystem.BossScaling.Expert", npc.GetFullNetName(), players, lifeBeforeLevel / players, lifeBeforeLevel, npc.lifeMax, level)
                : NetworkText.FromKey("Mods.ARPGEnemySystem.BossScaling.Normal", npc.GetFullNetName(), npc.lifeMax, level);

            ChatHelper.BroadcastChatMessage(text, Color.Orange);
        }
    }
}
