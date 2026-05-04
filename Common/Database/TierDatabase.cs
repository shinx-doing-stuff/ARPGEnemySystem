using ARPGEnemySystem.Common.GlobalNPCs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARPGEnemySystem.Common.Database
{
    public struct Tier
    {
        public int minValue;
        public int maxValue;
        public Tier(int min, int max)
        {
            minValue = min; maxValue = max;
        }
    }
    public static class TierDatabase
    {
        public static Dictionary<Enum, List<Tier>> modifierTierDatabase = new Dictionary<Enum, List<Tier>>()
        {
            {ModifierType.Colossal, new List<Tier>
            {
                new Tier(56,60),
                new Tier(51,55),
                new Tier(46,50),
                new Tier(41,45),
                new Tier(36,40),
                new Tier(31,35),
                new Tier(26,30),
                new Tier(21,25),
                new Tier(16,20),
                new Tier(10,15)
            } },
            {ModifierType.Tiny, new List<Tier>
            {
                new Tier(56,60),
                new Tier(51,55),
                new Tier(46,50),
                new Tier(41,45),
                new Tier(36,40),
                new Tier(31,35),
                new Tier(26,30),
                new Tier(21,25),
                new Tier(16,20),
                new Tier(10,15)
            } },
            {ModifierType.Strong, new List<Tier>
            {
                new Tier(56,60),
                new Tier(51,55),
                new Tier(46,50),
                new Tier(41,45),
                new Tier(36,40),
                new Tier(31,35),
                new Tier(26,30),
                new Tier(21,25),
                new Tier(16,20),
                new Tier(10,15)
            } },
            {ModifierType.Durable, new List<Tier>
            {
                new Tier(56,60),
                new Tier(51,55),
                new Tier(46,50),
                new Tier(41,45),
                new Tier(36,40),
                new Tier(31,35),
                new Tier(26,30),
                new Tier(21,25),
                new Tier(16,20),
                new Tier(10,15)
            } },
            {ModifierType.Quick, new List<Tier>
            {
                new Tier(36,40),
                new Tier(33,35),
                new Tier(30,32),
                new Tier(27,29),
                new Tier(24,26),
                new Tier(21,23),
                new Tier(18,20),
                new Tier(15,17),
                new Tier(12,14),
                new Tier(10,11)
            } },
            {ModifierType.SoulDrinker, new List<Tier>
            {
                new Tier(56,60),
                new Tier(51,55),
                new Tier(46,50),
                new Tier(41,45),
                new Tier(36,40),
                new Tier(31,35),
                new Tier(26,30),
                new Tier(21,25),
                new Tier(16,20),
                new Tier(10,15)
            } },
            // Elemental damage modifiers — magnitude = % of base damage dealt as that element
            {ModifierType.Flaming, new List<Tier>
            {
                new Tier(36,40),
                new Tier(33,35),
                new Tier(30,32),
                new Tier(27,29),
                new Tier(24,26),
                new Tier(21,23),
                new Tier(18,20),
                new Tier(15,17),
                new Tier(12,14),
                new Tier(10,11)
            } },
            {ModifierType.Glacial, new List<Tier>
            {
                new Tier(36,40),
                new Tier(33,35),
                new Tier(30,32),
                new Tier(27,29),
                new Tier(24,26),
                new Tier(21,23),
                new Tier(18,20),
                new Tier(15,17),
                new Tier(12,14),
                new Tier(10,11)
            } },
            {ModifierType.Charged, new List<Tier>
            {
                new Tier(36,40),
                new Tier(33,35),
                new Tier(30,32),
                new Tier(27,29),
                new Tier(24,26),
                new Tier(21,23),
                new Tier(18,20),
                new Tier(15,17),
                new Tier(12,14),
                new Tier(10,11)
            } },
            // Elemental resistance modifiers — magnitude = flat % added to that element's resistance
            {ModifierType.FireResistant, new List<Tier>
            {
                new Tier(36,40),
                new Tier(33,35),
                new Tier(30,32),
                new Tier(27,29),
                new Tier(24,26),
                new Tier(21,23),
                new Tier(18,20),
                new Tier(15,17),
                new Tier(12,14),
                new Tier(10,11)
            } },
            {ModifierType.ColdResistant, new List<Tier>
            {
                new Tier(36,40),
                new Tier(33,35),
                new Tier(30,32),
                new Tier(27,29),
                new Tier(24,26),
                new Tier(21,23),
                new Tier(18,20),
                new Tier(15,17),
                new Tier(12,14),
                new Tier(10,11)
            } },
            {ModifierType.LightningResistant, new List<Tier>
            {
                new Tier(36,40),
                new Tier(33,35),
                new Tier(30,32),
                new Tier(27,29),
                new Tier(24,26),
                new Tier(21,23),
                new Tier(18,20),
                new Tier(15,17),
                new Tier(12,14),
                new Tier(10,11)
            } },
            // Penetration modifiers — magnitude = flat % subtracted from player resistance
            // (or % of statDefense reduced for Sundering, before physRes is computed).
            // Tier shape matches Flaming/Glacial/Charged/FireResistant exactly.
            {ModifierType.Searing, new List<Tier>
            {
                new Tier(36,40),
                new Tier(33,35),
                new Tier(30,32),
                new Tier(27,29),
                new Tier(24,26),
                new Tier(21,23),
                new Tier(18,20),
                new Tier(15,17),
                new Tier(12,14),
                new Tier(10,11)
            } },
            {ModifierType.Shattering, new List<Tier>
            {
                new Tier(36,40),
                new Tier(33,35),
                new Tier(30,32),
                new Tier(27,29),
                new Tier(24,26),
                new Tier(21,23),
                new Tier(18,20),
                new Tier(15,17),
                new Tier(12,14),
                new Tier(10,11)
            } },
            {ModifierType.Conductive, new List<Tier>
            {
                new Tier(36,40),
                new Tier(33,35),
                new Tier(30,32),
                new Tier(27,29),
                new Tier(24,26),
                new Tier(21,23),
                new Tier(18,20),
                new Tier(15,17),
                new Tier(12,14),
                new Tier(10,11)
            } },
            {ModifierType.Sundering, new List<Tier>
            {
                new Tier(36,40),
                new Tier(33,35),
                new Tier(30,32),
                new Tier(27,29),
                new Tier(24,26),
                new Tier(21,23),
                new Tier(18,20),
                new Tier(15,17),
                new Tier(12,14),
                new Tier(10,11)
            } },
        };
    }
}
