using ARPGEnemySystem.Common.Configs;
using ARPGEnemySystem.Common.Elements;
using ARPGEnemySystem.Common.Scaling;
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

        // Elemental
        public float FireDamagePct      = 0f;
        public float ColdDamagePct      = 0f;
        public float LightningDamagePct = 0f;
        public float FireResistance     = 0f;
        public float ColdResistance     = 0f;
        public float LightningResistance = 0f;
        // Physical resistance derived at hit time from npc.defense via ConvertDefenseToResistance

        // Penetration
        // Bosses don't roll modifiers, so this is the only source of pen for them.
        public float FirePen      = 0f;
        public float ColdPen      = 0f;
        public float LightningPen = 0f;
        public float SunderingPct = 0f;

        // Chaos — intentionally low magnitude per 2026-05-13-chaos-damage-type-design spec
        public float ChaosDamagePct  = 0f;
        public float ChaosResistance = 0f;
        public float ChaosPen        = 0f;

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
                int phase = ScalingMath.GetScalingPhase();

                var s = new EnemyStatBlock { LifeMax = npc.lifeMax, Damage = npc.damage, Defense = npc.defense };

                int lifeBeforeLevel = npc.lifeMax;
                EnemyScaling.ApplyLevelScaling(ref s, level, phase, cfg.ScalingExponent, cfg.DefScalingExponent, cfg.DefenseFloor);

                npc.lifeMax = s.LifeMax;
                npc.life    = npc.lifeMax;
                npc.damage  = s.Damage;
                npc.defense = s.Defense;
                statChanged = true;
                BossPlayerScaling.Announce(npc, lifeBeforeLevel, level);

                EnemyScaling.ApplyBossElementals(ref s, EnemyScaling.BossElementalTier());

                FireResistance      = s.FireResistance;
                ColdResistance      = s.ColdResistance;
                LightningResistance = s.LightningResistance;
                FireDamagePct       = s.FireDamagePct;
                ColdDamagePct       = s.ColdDamagePct;
                LightningDamagePct  = s.LightningDamagePct;
                FirePen      = s.FirePen;
                ColdPen      = s.ColdPen;
                LightningPen = s.LightningPen;
                SunderingPct = s.SunderingPct;
                ChaosResistance = s.ChaosResistance;
                ChaosDamagePct  = s.ChaosDamagePct;
                ChaosPen        = s.ChaosPen;
            }
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
            binaryWriter.Write(ChaosDamagePct);
            binaryWriter.Write(ChaosResistance);
            binaryWriter.Write(ChaosPen);
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
            SunderingPct    = binaryReader.ReadSingle();
            ChaosDamagePct  = binaryReader.ReadSingle();
            ChaosResistance = binaryReader.ReadSingle();
            ChaosPen        = binaryReader.ReadSingle();
        }
    }
}
