using ARPGEnemySystem.Common.Configs;
using ARPGEnemySystem.Common.Elements;
using ARPGEnemySystem.Common.Scaling;
using ARPGEnemySystem.Common.Systems;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.ModLoader.UI.ModBrowser;
using Terraria.WorldBuilding;

namespace ARPGEnemySystem.Common.GlobalNPCs
{
    public class NPCManager : GlobalNPC
    {
        public override bool InstancePerEntity => true;
        public int level = 0;
        public bool statChanged = false;
        public List<EnemyModifier> modifierList = new List<EnemyModifier>();
        public EnemyRarity rarity = new EnemyRarity();

        // Elemental
        public float FireDamagePct      = 0f;
        public float ColdDamagePct      = 0f;
        public float LightningDamagePct = 0f;
        public float FireResistance     = 0f;
        public float ColdResistance     = 0f;
        public float LightningResistance = 0f;
        // Physical resistance is derived at hit time from npc.defense via ElementalMath.ConvertDefenseToResistance

        // Penetration — baseline from rarity (SetDefaults) + modifier-rolled magnitude (PreAI, +=).
        public float FirePen      = 0f;
        public float ColdPen      = 0f;
        public float LightningPen = 0f;
        public float SunderingPct = 0f;

        // Chaos — intentionally low magnitude (see 2026-05-13-chaos-damage-type-design spec)
        public float ChaosDamagePct  = 0f;
        public float ChaosResistance = 0f;
        public float ChaosPen        = 0f;

        // Only applies to normal enemy
        public override bool AppliesToEntity(NPC entity, bool lateInstantiation)
        {
            return !entity.townNPC && !entity.friendly && !entity.CountsAsACritter && !entity.boss && entity.type != NPCID.TargetDummy;
        }
        public override GlobalNPC Clone(NPC from, NPC to)
        {
            var clone = base.Clone(from, to);
            ((NPCManager)clone).modifierList = modifierList.ToList();
            return clone;
        }

        public override void SetDefaults(NPC entity)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                Random rand = new Random();
                level = Math.Clamp(rand.Next((int)(WorldManager.levelCap*0.75f), (int)(WorldManager.levelCap*1.1f)), 1, (int)(WorldManager.levelCap * 1.1f) + 1);
                AddModifier(entity);

                // Elemental resistance/pen baseline from rarity. Modifier bonuses (FireResistant/
                // Searing/etc.) are additive on top, applied in PreAI.
                var s = new EnemyStatBlock();
                EnemyScaling.ApplyRarityElementals(ref s, rarity.rarity);
                FireResistance      = s.FireResistance;
                ColdResistance      = s.ColdResistance;
                LightningResistance = s.LightningResistance;
                FirePen      = s.FirePen;
                ColdPen      = s.ColdPen;
                LightningPen = s.LightningPen;
                SunderingPct = s.SunderingPct;
                ChaosResistance = s.ChaosResistance;
                ChaosPen        = s.ChaosPen;
            }
        }

        public void AddModifier(NPC npc)
        {
            modifierList.Clear();
            for (int i = 0; i < Utils.GetAmountOfEnemyModifier(rarity); i++)
            {
                List<int> excludeList = Utils.CreateExcludeList(modifierList);
                int tier = Utils.GetTier();
                modifierList.Add(new EnemyModifier(excludeList, tier));
            }
        }

        public override bool PreAI(NPC npc)
        {
            if (statChanged) return true;

            var cfg = ModContent.GetInstance<Config>();
            int phase = ScalingMath.GetScalingPhase();

            var s = new EnemyStatBlock
            {
                LifeMax = npc.lifeMax,
                Damage = npc.damage,
                Defense = npc.defense,
                Scale = npc.scale,
                FireDamagePct = FireDamagePct,
                ColdDamagePct = ColdDamagePct,
                LightningDamagePct = LightningDamagePct,
                ChaosDamagePct = ChaosDamagePct,
                FireResistance = FireResistance,
                ColdResistance = ColdResistance,
                LightningResistance = LightningResistance,
                ChaosResistance = ChaosResistance,
                FirePen = FirePen,
                ColdPen = ColdPen,
                LightningPen = LightningPen,
                ChaosPen = ChaosPen,
                SunderingPct = SunderingPct,
            };

            int lifeBeforeLevel = npc.lifeMax;
            EnemyScaling.ApplyLevelScaling(ref s, level, phase, cfg.ScalingExponent, cfg.DefScalingExponent, cfg.DefenseFloor);
            EnemyScaling.ApplyRarityStats(ref s, rarity.rarity);
            npc.value *= Utils.GetCoinMultiplier(rarity, level, modifierList.Count);

            // Modifier effects
            foreach (var modifier in modifierList)
                EnemyScaling.ApplyModifier(ref s, modifier.modifierType, modifier.magnitude);

            npc.lifeMax = s.LifeMax;
            npc.life    = npc.lifeMax;
            npc.damage  = s.Damage;
            npc.defense = s.Defense;
            npc.scale   = s.Scale;
            FireDamagePct       = s.FireDamagePct;
            ColdDamagePct       = s.ColdDamagePct;
            LightningDamagePct  = s.LightningDamagePct;
            ChaosDamagePct      = s.ChaosDamagePct;
            FireResistance      = s.FireResistance;
            ColdResistance      = s.ColdResistance;
            LightningResistance = s.LightningResistance;
            ChaosResistance     = s.ChaosResistance;
            FirePen      = s.FirePen;
            ColdPen      = s.ColdPen;
            LightningPen = s.LightningPen;
            ChaosPen     = s.ChaosPen;
            SunderingPct = s.SunderingPct;

            statChanged = true;
            BossPlayerScaling.Announce(npc, lifeBeforeLevel, level);
            return true;
        }


        public override void ModifyIncomingHit(NPC npc, ref NPC.HitModifiers modifiers)
        {
            // Hook ordering guarantee: ARPGItemSystem's ModifyHitNPC (attacker) reads
            // target.defense BEFORE this defender hook zeroes it.
            modifiers.Defense *= 0f;
            // Armor penetration is left untouched — with defense=0, pen has no effect on the vanilla formula,
            // and zeroing it would remove legitimate pen from vanilla accessories and other mods.
        }

        public override void OnHitPlayer(NPC npc, Player target, Player.HurtInfo hurtInfo)
        {
            foreach (var modifier in modifierList)
            {
                switch (modifier.modifierType)
                {
                    case ModifierType.SoulDrinker:
                        target.statMana -= modifier.magnitude;
                        break;
                }
            }
        }

        public override void SendExtraAI(NPC npc, BitWriter bitWriter, BinaryWriter binaryWriter)
        {
            binaryWriter.Write7BitEncodedInt(level);
            binaryWriter.Write7BitEncodedInt((int)rarity.rarity);

            List<int> modifierIDList, modifierMagnitudeList;
            SerializeData(out modifierIDList, out modifierMagnitudeList);

            binaryWriter.Write(modifierList.Count);
            foreach (var modifierID in modifierIDList)
            {
                binaryWriter.Write(modifierID);
            }
            binaryWriter.Write(modifierMagnitudeList.Count);
            foreach (var modifierMagnitude in modifierMagnitudeList)
            {
                binaryWriter.Write(modifierMagnitude);
            }
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
            rarity = new EnemyRarity((Rarity)binaryReader.Read7BitEncodedInt());

            List<int> modifierIDList = new List<int>(), modifierMagnitudeList = new List<int>();

            var modifierIDListCount = binaryReader.ReadInt32();
            for (int i = 0; i < modifierIDListCount; i++)
            {
                modifierIDList.Add(binaryReader.ReadInt32());
            }
            var modifierMagnitudeListCount = binaryReader.ReadInt32();
            for (int i = 0; i < modifierMagnitudeListCount; i++)
            {
                modifierMagnitudeList.Add(binaryReader.ReadInt32());
            }

            modifierList.Clear();
            for (int i = 0; i < modifierIDList.Count; i++)
            {
                modifierList.Add(new EnemyModifier((ModifierType)modifierIDList[i], modifierMagnitudeList[i]));
            }
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

        private void SerializeData(out List<int> modifierIDList, out List<int> modifierMagnitudeList)
        {
            modifierIDList = new List<int>();
            modifierMagnitudeList = new List<int>();
            foreach (var modifier in modifierList)
            {
                modifierIDList.Add((int)modifier.modifierType);
                modifierMagnitudeList.Add(modifier.magnitude);
            }
        }
    }
}
