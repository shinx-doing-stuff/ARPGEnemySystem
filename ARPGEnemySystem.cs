using ARPGEnemySystem.Common.Network;
using ARPGEnemySystem.Common.Systems;
using System;
using System.IO;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace ARPGEnemySystem
{
	public class ARPGEnemySystem : Mod
	{
		// ARPG Item System is a mutual hard requirement, but it cannot be declared via
		// build.txt modReferences (would create a load-order cycle with ItemSystem's
		// modReferences = ARPGEnemySystem). Enforced here instead — runs after every
		// mod's Load() so HasMod sees the final loaded set.
		public override void PostSetupContent()
		{
			if (!ModLoader.HasMod("ARPGItemSystem"))
				throw new Exception("ARPG Enemy System requires ARPG Item System to be installed and enabled.");
		}

		public override void HandlePacket(BinaryReader reader, int whoAmI)
		{
			EnemyPacketType type = (EnemyPacketType)reader.ReadByte();
			switch (type)
			{
				case EnemyPacketType.LevelCap:
					int levelCap = reader.ReadInt32();
					if (Main.netMode == NetmodeID.MultiplayerClient)
						WorldManager.levelCap = levelCap;
					break;
				default:
					Logger.Warn($"Unknown packet type {type}");
					break;
			}
		}
	}
}
