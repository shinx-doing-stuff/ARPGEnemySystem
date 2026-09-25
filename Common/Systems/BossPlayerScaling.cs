using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace ARPGEnemySystem.Common.Systems
{
    // Vanilla's Expert/Master boss life grows by less than one boss per extra player
    // (1.35x at 2 players, 2.63x at 4). Bosses and minibosses from the Boss Checklist
    // roster get solo life x player count instead. Normal mode, boss limbs and
    // mid-fight joins stay vanilla.
    public class BossPlayerScaling : ModSystem
    {
        public override void Load()
        {
            On_NPC.ScaleStats += ScaleStats;
        }

        private void ScaleStats(On_NPC.orig_ScaleStats orig, NPC self, int? activePlayersCount, GameModeData gameModeData, float? strengthOverride)
        {
            if (!BossRoster.ScalesWithPlayers(self.type))
            {
                orig(self, activePlayersCount, gameModeData, strengthOverride);
                return;
            }

            // Scale as a solo fight so vanilla's and the boss mod's own difficulty tuning still apply.
            int players = activePlayersCount ?? NPC.GetActivePlayerCount();
            orig(self, 1, gameModeData, strengthOverride);
            int soloLife = self.lifeMax;

            // Vanilla only records a player count when it ran its Expert multiplayer step (never in Normal mode).
            bool expert = self.statsAreScaledForThisManyPlayers == 1;
            if (expert && players > 1)
            {
                // Golem's AI and the NPC spawn packet read the stored count, so it must be the real one.
                self.statsAreScaledForThisManyPlayers = players;
                self.lifeMax *= players;
                self.life = self.lifeMax;
            }

            if (IsWorldNpc(self))
            {
                string note = expert ? "before level scaling" : "Normal mode, not player-scaled";
                Mod.Logger.Info($"Boss player scaling: {self.FullName} ({self.type}), {players} player(s), solo life {soloLife} -> {self.lifeMax} ({note})");
            }
        }

        // Moon Lord's boss bar, the bestiary and content samples scale throwaway NPC copies; only log real ones.
        private static bool IsWorldNpc(NPC npc)
        {
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                if (Main.npc[i] == npc)
                    return true;
            }
            return false;
        }
    }
}
