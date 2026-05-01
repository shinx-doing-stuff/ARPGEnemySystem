using ARPGEnemySystem.Common.Configs;
using ARPGEnemySystem.Common.Elements;
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

        // Elemental — only populated when ARPGItemSystem is loaded
        public Element ElementalDamageType  = Element.Physical;
        public float   ElementalDamagePct   = 0f;
        public float   FireResistance       = 0f;
        public float   ColdResistance       = 0f;
        public float   LightningResistance  = 0f;
        // Physical resistance derived at hit time from npc.defense via ConvertDefenseToResistance

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

                var cfg = ModContent.GetInstance<Config>();
                int phase = WorldManager.GetScalingPhase();
                float multiplier    = 1f + MathF.Pow(level, cfg.ScalingExponent)            * WorldManager.PhaseRates[phase];
                float defMultiplier = 1f + MathF.Pow(level, cfg.DefScalingExponent) * WorldManager.DefPhaseRates[phase];

                npc.lifeMax = (int)(npc.lifeMax * multiplier);
                npc.life    = npc.lifeMax;
                npc.damage  = (int)(npc.damage  * multiplier);
                npc.defense += (int)(level * cfg.DefenseFloor);
                npc.defense  = (int)(npc.defense * defMultiplier);
                statChanged = true;

                if (ModLoader.HasMod("ARPGItemSystem"))
                {
                    var cap = cfg.ElementalResistanceCap;
                    bool postPlantera = WorldManager.downedBossIDs.Contains(NPCID.Plantera);
                    int tier = postPlantera ? 2 : Main.hardMode ? 1 : 0;

                    float[] elemResValues   = { 25f, 50f, 75f };
                    float[] damagePctValues = { 25f, 50f, 75f };

                    FireResistance      = Math.Min(elemResValues[tier], cap);
                    ColdResistance      = Math.Min(elemResValues[tier], cap);
                    LightningResistance = Math.Min(elemResValues[tier], cap);
                    ElementalDamagePct  = damagePctValues[tier];
                    ElementalDamageType = (Element)(Main.rand.Next(3) + 1);
                }
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
            binaryWriter.Write((byte)ElementalDamageType);
            binaryWriter.Write(ElementalDamagePct);
            binaryWriter.Write(FireResistance);
            binaryWriter.Write(ColdResistance);
            binaryWriter.Write(LightningResistance);
        }

        // Make sure you always read exactly as much data as you sent!
        public override void ReceiveExtraAI(NPC npc, BitReader bitReader, BinaryReader binaryReader)
        {
            level = binaryReader.Read7BitEncodedInt();
            npc.lifeMax = binaryReader.Read7BitEncodedInt();
            npc.life = binaryReader.Read7BitEncodedInt();
            npc.defense = binaryReader.Read7BitEncodedInt();
            npc.damage = binaryReader.Read7BitEncodedInt();
            ElementalDamageType  = (Element)binaryReader.ReadByte();
            ElementalDamagePct   = binaryReader.ReadSingle();
            FireResistance       = binaryReader.ReadSingle();
            ColdResistance       = binaryReader.ReadSingle();
            LightningResistance  = binaryReader.ReadSingle();
        }
    }
}
