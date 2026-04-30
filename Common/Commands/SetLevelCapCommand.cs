using ARPGEnemySystem.Common.Systems;
using Terraria.ModLoader;

namespace ARPGEnemySystem.Common.Commands
{
    public class SetLevelCapCommand : ModCommand
    {
        public override CommandType Type
            => CommandType.Chat;
        public override string Command
            => "setlevelcap";
        public override string Description
            => "Set world level cap for testing. Usage: /setlevelcap <value>";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (args.Length == 0 || !int.TryParse(args[0], out int value) || value < 0)
            {
                caller.Reply("Usage: /setlevelcap <value>  (non-negative integer)");
                return;
            }
            WorldManager.levelCap = value;
            caller.Reply($"Level cap set to {value}. New enemies will spawn at level ~{value}.");
        }
    }
}
