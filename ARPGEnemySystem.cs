using ARPGEnemySystem.Common.GlobalNPCs;
using ARPGEnemySystem.Common.Network;
using ARPGEnemySystem.Common.Scaling;
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

		// Read-only info for presentation mods (Fancy Healthbar). Returns null for an NPC this mod does not manage.
		public override object Call(params object[] args)
		{
			if (args.Length >= 1 && args[0] is string name && name == "GetEnemyInfo")
			{
				if (args.Length >= 2 && args[1] is NPC npc && npc.TryGetGlobalNPC(out EnemyProfileNPC data))
				{
					// Level 0 = managed but not yet synced to this client; callers keep polling.
					if (data.Profile == null)
						return new int[] { 0, 0 };
					int rarityTier = data.Profile.Kind == EnemyKind.FightMember ? 0 : (int)data.Profile.Rarity;
					return new int[] { data.Profile.Level, rarityTier };
				}
				return null;
			}
			Logger.Warn($"Unknown Mod.Call: {(args.Length > 0 ? args[0] : "<none>")}");
			return null;
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
