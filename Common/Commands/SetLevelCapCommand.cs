using ARPGEnemySystem.Common.Systems;
using Terraria.ModLoader;

namespace ARPGEnemySystem.Common.Commands
{
    public class SetLevelCapCommand : ModCommand
    {
        public override CommandType Type
            => CommandType.World;
        public override string Command
            => "setlevelcap";
        public override string Description
            => "Hold the world level for testing. Usage: /setlevelcap <value> | auto";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (args.Length > 0 && args[0] == "auto")
            {
                WorldManager.levelCapOverride = -1;
                WorldManager.Recompute(announce: false);
                caller.Reply($"World level follows boss progression again ({WorldManager.levelCap}).");
                return;
            }

            if (args.Length == 0 || !int.TryParse(args[0], out int value) || value < 0)
            {
                caller.Reply("Usage: /setlevelcap <value> | auto");
                return;
            }

            WorldManager.levelCapOverride = value;
            WorldManager.levelCap = value;
            WorldManager.SendLevelCap();
            caller.Reply($"World level held at {value}. New enemies will spawn at level ~{value}.");
        }
    }
}
