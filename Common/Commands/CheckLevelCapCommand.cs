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
