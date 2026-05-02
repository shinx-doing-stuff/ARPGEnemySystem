using ARPGEnemySystem.Common.Database;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;

namespace ARPGEnemySystem.Common.GlobalNPCs
{
    public enum Rarity
    {
        None,       // 0
        Common,     // 1
        Uncommon,   // 2
        Rare,       // 3
        Elite,      // 4
        Legend,     // 5
    }

    public static class RarityDatabase
    {
        // Elemental resistance baseline per rarity — same value for all three elements.
        // Stored as raw %; clamped to cap at hit time. Modifier bonuses stack on top.
        public static Dictionary<Rarity, int> rarityElementalResDatabase = new Dictionary<Rarity, int>()
        {
            { Rarity.None,      0  },
            { Rarity.Common,    0  },
            { Rarity.Uncommon,  10 },
            { Rarity.Rare,      20 },
            { Rarity.Elite,     30 },
            { Rarity.Legend,    40 },
        };

        // List<int> entries: [0] = HP%, [1] = Defense%, [2] = Damage%
        public static Dictionary<Rarity, List<int>> rarityModifierDatabase = new Dictionary<Rarity, List<int>>()
        {
            { Rarity.Common,   new List<int> {   0,   0,  0 } },
            { Rarity.Uncommon, new List<int> {  20,  10, 10 } },
            { Rarity.Rare,     new List<int> {  50,  25, 20 } },
            { Rarity.Elite,    new List<int> { 70,  40, 30 } },
            { Rarity.Legend,   new List<int> { 100, 70, 50 } },
        };

        // 8 weight columns, one per boss milestone (matches GetWeightIndex()).
        // Each column must sum to 100.
        public static Dictionary<Rarity, List<int>> rarityWeightDatabase = new Dictionary<Rarity, List<int>>()
        {
            //                          pre SlimeK  Skele   WoF  QSlime MechAny Golem Plant+
            { Rarity.Common,   new List<int> { 70,  60,  55,  50,  40,  30,  20,  10 } },
            { Rarity.Uncommon, new List<int> { 20,  25,  25,  25,  25,  25,  20,  15 } },
            { Rarity.Rare,     new List<int> {  8,  10,  13,  15,  20,  22,  25,  25 } },
            { Rarity.Elite,    new List<int> {  2,   4,   5,   8,  12,  17,  25,  30 } },
            { Rarity.Legend,   new List<int> {  0,   1,   2,   2,   3,   6,  10,  15 } },
        };
    }

    public struct EnemyRarity
    {
        public Rarity rarity = Rarity.None;
        public List<int> magnitude = new List<int> { 0, 0, 0 }; // HP% // Defense% // Damage%

        public EnemyRarity(Rarity _rarity)
        {
            rarity = _rarity;
            magnitude = RarityDatabase.rarityModifierDatabase[rarity];
        }

        public EnemyRarity()
        {
            int i = GetWeightIndex();
            Random random = new Random();
            int roll = random.Next(0, 100);
            foreach (KeyValuePair<Rarity, List<int>> item in RarityDatabase.rarityWeightDatabase)
            {
                if (roll > item.Value[i] - 1)
                {
                    roll -= item.Value[i];
                    continue;
                }
                else
                {
                    rarity = item.Key;
                    break;
                }
            }

            magnitude = RarityDatabase.rarityModifierDatabase[rarity];
        }

        public int GetWeightIndex()
        {
            int weightIndex = 0;
            if (NPC.downedSlimeKing)    weightIndex += 1; // 1
            if (NPC.downedBoss3)        weightIndex += 1; // 2
            if (Main.hardMode)          weightIndex += 1; // 3
            if (NPC.downedQueenSlime)   weightIndex += 1; // 4
            if (NPC.downedMechBossAny)  weightIndex += 1; // 5
            if (NPC.downedGolemBoss)    weightIndex += 1; // 6
            if (NPC.downedPlantBoss)    weightIndex += 1; // 7
            return weightIndex;
        }
    }
}
