using ARPGEnemySystem.Common.Database;
using ARPGEnemySystem.Common.GlobalNPCs;
using ARPGEnemySystem.Common.Systems;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria;
using Terraria.Chat;
using Terraria.ID;

namespace ARPGEnemySystem.Common
{
    public class Utils
    {
        internal static List<int> CreateExcludeList(List<EnemyModifier> modifierList)
        {
            var excludeModifierListEnum = modifierList.Select(o => o.modifierType).ToList();
            List<int> excludeListInt = new List<int>();

            foreach (var item in excludeModifierListEnum)
            {
                excludeListInt.Add((int)item);
            }

            return excludeListInt;
        }
        internal static int GetAmountOfEnemyModifier(EnemyRarity rarity)
        {
            int rarityBonus = rarity.rarity switch
            {
                Rarity.Common    => 0,
                Rarity.Uncommon  => 1,
                Rarity.Rare      => 1,
                Rarity.Elite     => 2,
                Rarity.Legend    => 3,
                _                => 0,
            };
            int floorByRarity = rarity.rarity switch
            {
                Rarity.Elite     => 1,
                Rarity.Legend    => 2,
                _                => 0,
            };
            int phase = WorldManager.GetScalingPhase(); // 0..3

            int actualMax = Math.Min(8, 2 + rarityBonus + phase);
            return new Random().Next(floorByRarity, actualMax + 1);
        }
        internal static int GetTier()
        {
            Random random = new Random();
            int maximumTier = 8;
            int minimumTier = 10;
            if (NPC.downedSlimeKing) maximumTier -= 1;
            if (NPC.downedBoss2) minimumTier -= 1;
            if (NPC.downedBoss3) maximumTier -= 1;
            if (Main.hardMode) maximumTier -= 1;
            if (NPC.downedQueenSlime) minimumTier -= 1;
            if (NPC.downedMechBossAny) maximumTier -= 1;
            if (NPC.downedGolemBoss) minimumTier -= 1;
            if (NPC.downedPlantBoss) { maximumTier -= 1; }
            if (NPC.downedFishron) { minimumTier -= 1; }
            if (NPC.downedEmpressOfLight) { maximumTier -= 1; minimumTier -= 1; }
            if (NPC.downedAncientCultist) { maximumTier -= 1; }
            if (NPC.downedMoonlord) { maximumTier -= 1; minimumTier -= 1; }

            return random.Next(maximumTier, minimumTier);
        }
        internal static float GetCoinMultiplier(EnemyRarity rarity, int level, int modifierCount)
        {
            float rarityBonus   = 1.75f * rarity.magnitude[0] / 100f;
            float levelBonus    = 0.005f * level;
            float modifierBonus = 0.15f * modifierCount;
            return 1f + rarityBonus + levelBonus + modifierBonus;
        }
        /// <summary>
        /// XP multiplier from rarity, level, and modifier count. Initially identical to
        /// <see cref="GetCoinMultiplier"/>; kept as a separate symbol so coin and XP
        /// yields can be tuned independently.
        /// </summary>
        public static float GetXPMultiplier(EnemyRarity rarity, int level, int modifierCount)
        {
            float rarityBonus   = 1.75f * rarity.magnitude[0] / 100f;
            float levelBonus    = 0.005f * level;
            float modifierBonus = 0.15f * modifierCount;
            return 1f + rarityBonus + levelBonus + modifierBonus;
        }
        internal static bool IsDummy(NPC npc)
        {
            var nameSpan = NPCID.Search.GetName(npc.type).AsSpan();
            var index = nameSpan.IndexOf('/');
            if (index != -1)
                nameSpan = nameSpan[index..];
            return nameSpan.ToString().ToLowerInvariant().Contains("dummy");
        }
    }
}
