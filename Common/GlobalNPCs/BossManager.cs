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
        public float FireDamagePct      = 0f;
        public float ColdDamagePct      = 0f;
        public float LightningDamagePct = 0f;
        public float FireResistance     = 0f;
        public float ColdResistance     = 0f;
        public float LightningResistance = 0f;
        // Physical resistance derived at hit time from npc.defense via ConvertDefenseToResistance

        // Penetration — only populated when ARPGItemSystem is loaded.
        // Bosses don't roll modifiers, so this is the only source of pen for them.
        public float FirePen      = 0f;
        public float ColdPen      = 0f;
        public float LightningPen = 0f;
        public float SunderingPct = 0f;

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
                    float[] damageValues    = { 15f, 30f, 45f };
                    float[] penValues       = { 15f, 30f, 45f };

                    FireResistance      = Math.Min(elemResValues[tier], cap);
                    ColdResistance      = Math.Min(elemResValues[tier], cap);
                    LightningResistance = Math.Min(elemResValues[tier], cap);

                    // All three elemental damage types simultaneously (was: single random element).
                    // Damage values reduced from {25, 50, 75} → {15, 30, 45} to compensate for
                    // bonus model in PlayerHurtPipeline (no longer eats physical portion).
                    FireDamagePct      = damageValues[tier];
                    ColdDamagePct      = damageValues[tier];
                    LightningDamagePct = damageValues[tier];

                    // Penetration baseline tied to progression phase.
                    FirePen      = penValues[tier];
                    ColdPen      = penValues[tier];
                    LightningPen = penValues[tier];
                    SunderingPct = penValues[tier];
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
            binaryWriter.Write(FireDamagePct);
            binaryWriter.Write(ColdDamagePct);
            binaryWriter.Write(LightningDamagePct);
            binaryWriter.Write(FireResistance);
            binaryWriter.Write(ColdResistance);
            binaryWriter.Write(LightningResistance);
            binaryWriter.Write(FirePen);
            binaryWriter.Write(ColdPen);
            binaryWriter.Write(LightningPen);
            binaryWriter.Write(SunderingPct);
        }

        // Make sure you always read exactly as much data as you sent!
        public override void ReceiveExtraAI(NPC npc, BitReader bitReader, BinaryReader binaryReader)
        {
            level = binaryReader.Read7BitEncodedInt();
            npc.lifeMax = binaryReader.Read7BitEncodedInt();
            npc.life = binaryReader.Read7BitEncodedInt();
            npc.defense = binaryReader.Read7BitEncodedInt();
            npc.damage = binaryReader.Read7BitEncodedInt();
            FireDamagePct       = binaryReader.ReadSingle();
            ColdDamagePct       = binaryReader.ReadSingle();
            LightningDamagePct  = binaryReader.ReadSingle();
            FireResistance      = binaryReader.ReadSingle();
            ColdResistance      = binaryReader.ReadSingle();
            LightningResistance = binaryReader.ReadSingle();
            FirePen      = binaryReader.ReadSingle();
            ColdPen      = binaryReader.ReadSingle();
            LightningPen = binaryReader.ReadSingle();
            SunderingPct = binaryReader.ReadSingle();
        }
    }
}
