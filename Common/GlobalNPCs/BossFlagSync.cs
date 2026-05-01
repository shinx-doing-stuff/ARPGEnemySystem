using ARPGEnemySystem.Common.Systems;
using Terraria;
using Terraria.ModLoader;

namespace ARPGEnemySystem.Common.GlobalNPCs
{
    // Safety net for vanilla multi-segment bosses (EoW especially) where the last dying
    // segment may have npc.boss == false, preventing BossManager.OnKill from firing.
    // After every NPC death, checks if any vanilla downed-boss flag flipped and registers
    // the corresponding boss if it isn't already in downedBossIDs.
    // Modded bosses are handled by BossManager.OnKill — this class only supplements vanilla.
    public class BossFlagSync : GlobalNPC
    {
        public override void OnKill(NPC npc)
        {
            WorldManager.SyncDownedFlags(announce: true);
        }
    }
}
