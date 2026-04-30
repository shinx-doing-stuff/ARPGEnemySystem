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

        // Elemental — only populated when ARPGItemSystem is loaded
        public Element ElementalDamageType  = Element.Physical;
        public float   ElementalDamagePct   = 0f;
        public float   FireResistance       = 0f;
        public float   ColdResistance       = 0f;
        public float   LightningResistance  = 0f;
        // Physical resistance is derived at hit time from npc.defense via ElementalMath.ConvertDefenseToResistance

        // Only applies to normal enemy
        public override bool AppliesToEntity(NPC entity, bool lateInstantiation)
        {
            return !entity.townNPC && !entity.CountsAsACritter && !entity.boss && entity.type != NPCID.TargetDummy;
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
                if (ModContent.GetInstance<Config>().ModifierAllowed) AddModifier(entity);

                if (ModLoader.HasMod("ARPGItemSystem"))
                {
                    var cfg = ModContent.GetInstance<Config>();
                    float cap = cfg.ElementalResistanceCap;

                    // Elemental resistances scale with level
                    float elemRes = Math.Min(level * cfg.EnemyElemResPerLevel * 100f, cap);
                    FireResistance      = elemRes;
                    ColdResistance      = elemRes;
                    LightningResistance = elemRes;

                    // Random chance to deal elemental damage
                    if (Main.rand.Next(100) < cfg.EnemyElementalChance)
                    {
                        ElementalDamageType = (Element)(Main.rand.Next(3) + 1); // 1=Fire 2=Cold 3=Lightning
                        ElementalDamagePct  = cfg.EnemyBaseElementalAllocationPct;
                    }
                    else
                    {
                        ElementalDamageType = Element.Physical;
                        ElementalDamagePct  = 0f;
                    }
                }
            }
        }

        public void AddModifier(NPC npc)
        {
            modifierList.Clear();
            for (int i = 0; i < Utils.GetAmountOfEnemyModifier(); i++)
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
                    case ModifierType.Burning:
                        ModifierDrawEffect.DrawBurning(npc);
                        break;
                    case ModifierType.Poisonous:
                        ModifierDrawEffect.DrawVenom(npc);
                        break;
                    case ModifierType.Frosty:
                        ModifierDrawEffect.DrawFrost(npc);
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
            // Apply rarity
            npc.lifeMax += (int)(npc.lifeMax * rarity.magnitude[0] / 100f);
            npc.life = npc.lifeMax;
            npc.defense += (int)(npc.defense * rarity.magnitude[1] / 100f);
            npc.damage += (int)(npc.damage * rarity.magnitude[2] / 100f);
            npc.value *= 1 + 1.5f * rarity.magnitude[0] / 100f;

            // Apply level
            npc.lifeMax += (int)(npc.lifeMax * level * ModContent.GetInstance<Config>().NormalEnemyHPIncreasePerLevel);
            npc.life = npc.lifeMax;
            npc.defense += (int)(npc.defense * level * ModContent.GetInstance<Config>().NormalEnemyDefenseIncreasePerLevel);
            npc.damage += (int)(npc.damage * level * ModContent.GetInstance<Config>().NormalEnemyDamageIncreasePerLevel);

            // Apply modifier
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
                    case ModifierType.Quick:
                        npc.velocity /= new Vector2(1 + modifier.magnitude / 100f, 1f);
                        break;
                }
            }

            statChanged = true;
            return true;
        }

        public override void PostAI(NPC npc)
        {
            foreach (var modifier in modifierList)
            {
                switch (modifier.modifierType)
                {
                    case ModifierType.Quick:
                        npc.velocity *= new Vector2(1 + modifier.magnitude / 100f, 1f);
                        break;
                }
            }
        }

        public override void ModifyIncomingHit(NPC npc, ref NPC.HitModifiers modifiers)
        {
            // Only bypass vanilla defense when ARPGItemSystem is loaded.
            // Without it, vanilla defense math should be preserved.
            // Hook ordering guarantee: ARPGItemSystem's ModifyHitNPC (attacker) reads
            // target.defense BEFORE this defender hook zeroes it.
            if (!ModLoader.HasMod("ARPGItemSystem")) return;

            modifiers.Defense *= 0f;
            modifiers.ScalingArmorPenetration += 0f;
            modifiers.ArmorPenetration += 0f;
        }

        public override void OnHitPlayer(NPC npc, Player target, Player.HurtInfo hurtInfo)
        {
            foreach (var modifier in modifierList)
            {
                switch (modifier.modifierType)
                {
                    case ModifierType.Poisonous:
                        target.AddBuff(BuffID.Poisoned, 300);
                        break;
                    case ModifierType.Burning:
                        target.AddBuff(BuffID.OnFire, 300);
                        break;
                    case ModifierType.Frosty:
                        target.AddBuff(BuffID.Frostburn, 120);
                        break;
                    case ModifierType.SoulDrinker:
                        target.statMana -= modifier.magnitude;
                        break;
                    case ModifierType.Destroyer:
                        target.AddBuff(BuffID.BrokenArmor, 120);
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
            ElementalDamageType  = (Element)binaryReader.ReadByte();
            ElementalDamagePct   = binaryReader.ReadSingle();
            FireResistance       = binaryReader.ReadSingle();
            ColdResistance       = binaryReader.ReadSingle();
            LightningResistance  = binaryReader.ReadSingle();
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
