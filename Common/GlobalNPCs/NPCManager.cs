using ARPGEnemySystem.Common.Configs;
using ARPGEnemySystem.Common.DrawEffects;
using ARPGEnemySystem.Common.Elements;
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

                // Elemental resistance baseline from rarity — same value for all three elements.
                // Modifier bonuses (FireResistant/ColdResistant/LightningResistant) are additive on top,
                // applied in PreAI. Values may exceed the cap; clamping happens at hit time.
                int rarityRes = RarityDatabase.rarityElementalResDatabase[rarity.rarity];
                FireResistance      = rarityRes;
                ColdResistance      = rarityRes;
                LightningResistance = rarityRes;
                // Elemental damage percentages are set by Flaming/Glacial/Charged modifiers in PreAI.
                // Penetration baseline per rarity — same value for all four fields.
                // Modifier bonuses (Searing/Shattering/Conductive/Sundering) stack on top in PreAI.
                int rarityPen = RarityDatabase.rarityElementalPenDatabase[rarity.rarity];
                FirePen      = rarityPen;
                ColdPen      = rarityPen;
                LightningPen = rarityPen;
                SunderingPct = rarityPen;
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

        public override void DrawEffects(NPC npc, ref Color drawColor)
        {
            foreach (var modifier in modifierList)
            {
                switch (modifier.modifierType)
                {
                    case ModifierType.Flaming:
                        ModifierDrawEffect.DrawFlaming(npc);
                        break;
                    case ModifierType.Glacial:
                        ModifierDrawEffect.DrawGlacial(npc);
                        break;
                    case ModifierType.Charged:
                        ModifierDrawEffect.DrawCharged(npc);
                        break;
                    case ModifierType.Durable:
                        drawColor.R = 75;
                        drawColor.G = 75;
                        drawColor.B = 75;
                        break;
                    case ModifierType.Strong:
                        drawColor.R = 255;
                        drawColor.G = 80;
                        drawColor.B = 80;
                        break;
                    case ModifierType.SoulDrinker:
                        ModifierDrawEffect.DrawSoulDrinker(npc);
                        break;
                }
            }
        }

        public override bool PreAI(NPC npc)
        {
            if (statChanged) return true;

            var cfg = ModContent.GetInstance<Config>();
            int phase = WorldManager.GetScalingPhase();
            float multiplier    = 1f + MathF.Pow(level, cfg.ScalingExponent)        * WorldManager.PhaseRates[phase];
            float defMultiplier = 1f + MathF.Pow(level, cfg.DefScalingExponent) * WorldManager.DefPhaseRates[phase];

            // Level scaling (exponential) — defense uses a steeper curve than HP/damage
            npc.lifeMax = (int)(npc.lifeMax * multiplier);
            npc.life    = npc.lifeMax;
            npc.damage  = (int)(npc.damage  * multiplier);
            // Additive floor ensures low-defense enemies (zombies, slimes) get baseline physRes
            // while preserving relative differences between enemy types.
            npc.defense += (int)(level * cfg.DefenseFloor);
            npc.defense  = (int)(npc.defense * defMultiplier);

            // Rarity bonus on top of scaled stats
            npc.lifeMax  += (int)(npc.lifeMax  * rarity.magnitude[0] / 100f);
            npc.life      = npc.lifeMax;
            npc.defense  += (int)(npc.defense  * rarity.magnitude[1] / 100f);
            npc.damage   += (int)(npc.damage   * rarity.magnitude[2] / 100f);
            npc.value    *= Utils.GetCoinMultiplier(rarity, level, modifierList.Count);

            // Modifier effects
            foreach (var modifier in modifierList)
            {
                switch (modifier.modifierType)
                {
                    case ModifierType.Colossal:
                        npc.scale = 1 + modifier.magnitude / 100f;
                        npc.lifeMax += (int)(npc.lifeMax * modifier.magnitude / 150f);
                        npc.life = npc.lifeMax;
                        break;
                    case ModifierType.Tiny:
                        npc.scale = 1 - modifier.magnitude / 100f;
                        npc.lifeMax -= (int)(npc.lifeMax * modifier.magnitude / 200f);
                        npc.life = npc.lifeMax;
                        break;
                    case ModifierType.Strong:
                        npc.damage += (int)(npc.damage * modifier.magnitude / 100f);
                        break;
                    case ModifierType.Durable:
                        npc.defense += (int)(npc.defense * modifier.magnitude / 100f);
                        break;
                    case ModifierType.Flaming:
                        FireDamagePct += modifier.magnitude;
                        break;
                    case ModifierType.Glacial:
                        ColdDamagePct += modifier.magnitude;
                        break;
                    case ModifierType.Charged:
                        LightningDamagePct += modifier.magnitude;
                        break;
                    case ModifierType.FireResistant:
                        FireResistance += modifier.magnitude;
                        break;
                    case ModifierType.ColdResistant:
                        ColdResistance += modifier.magnitude;
                        break;
                    case ModifierType.LightningResistant:
                        LightningResistance += modifier.magnitude;
                        break;
                    case ModifierType.Searing:
                        FirePen += modifier.magnitude;
                        break;
                    case ModifierType.Shattering:
                        ColdPen += modifier.magnitude;
                        break;
                    case ModifierType.Conductive:
                        LightningPen += modifier.magnitude;
                        break;
                    case ModifierType.Sundering:
                        SunderingPct += modifier.magnitude;
                        break;
                }
            }

            statChanged = true;
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
            SunderingPct = binaryReader.ReadSingle();
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
