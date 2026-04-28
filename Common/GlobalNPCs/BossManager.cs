using ARPGEnemySystem.Common.Configs;
using ARPGEnemySystem.Common.Systems;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace ARPGEnemySystem.Common.GlobalNPCs
{
    public class BossManager : GlobalNPC
    {
        public override bool InstancePerEntity => true;
        public int level = 0;
        public bool statChanged = false;

        // Only applies to normal enemy
        public override bool AppliesToEntity(NPC entity, bool lateInstantiation)
        {
            return entity.boss;
        }

        public override void OnSpawn(NPC npc, IEntitySource source)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                Random rand = new Random();
                level = Math.Clamp(rand.Next(WorldManager.levelCap, (int)(WorldManager.levelCap * 1.25f)), 1, (int)(WorldManager.levelCap * 1.25f) + 1);

                if (statChanged) return;
                npc.lifeMax += (int)(npc.lifeMax * level * ModContent.GetInstance<Config>().BossHPIncreasePerLevel);
                npc.life = npc.lifeMax;
                npc.defense += (int)(npc.defense * level * ModContent.GetInstance<Config>().BossDefenseIncreasePerLevel);
                npc.damage += (int)(npc.damage * level * ModContent.GetInstance<Config>().BossDamageIncreasePerLevel);
                statChanged = true;
            }   
        }

        public override void OnKill(NPC npc)
        {
            WorldManager.DownedBoss(npc);
        }

        public override void SendExtraAI(NPC npc, BitWriter bitWriter, BinaryWriter binaryWriter)
        {
            binaryWriter.Write7BitEncodedInt(level);    
            binaryWriter.Write7BitEncodedInt(npc.lifeMax);    
            binaryWriter.Write7BitEncodedInt(npc.life);    
            binaryWriter.Write7BitEncodedInt(npc.defense);    
            binaryWriter.Write7BitEncodedInt(npc.damage);
        }

        // Make sure you always read exactly as much data as you sent!
        public override void ReceiveExtraAI(NPC npc, BitReader bitReader, BinaryReader binaryReader)
        {
            level = binaryReader.Read7BitEncodedInt();
            npc.lifeMax = binaryReader.Read7BitEncodedInt();
            npc.life = binaryReader.Read7BitEncodedInt();
            npc.defense = binaryReader.Read7BitEncodedInt();
            npc.damage = binaryReader.Read7BitEncodedInt();
        }
    }
}
