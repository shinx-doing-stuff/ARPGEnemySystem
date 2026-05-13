using ARPGEnemySystem.Common.Database;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARPGEnemySystem.Common.GlobalNPCs
{
    public enum ModifierType
    {
        None,          // 0
        Colossal,      // 1  Increased Size + HP
        Tiny,          // 2  Decreased Size + HP
        Strong,        // 3  Increased Damage
        Durable,       // 4  Increased Defense
        SoulDrinker,   // 5  Burn Mana
        Flaming,       // 6  Deal Fire elemental damage
        Glacial,       // 7  Deal Cold elemental damage
        Charged,       // 8  Deal Lightning elemental damage
        FireResistant,       // 9  Increased Fire Resistance
        ColdResistant,       // 10 Increased Cold Resistance
        LightningResistant,  // 11 Increased Lightning Resistance
        Searing,       // 12 Penetrates player Fire resistance
        Shattering,    // 13 Penetrates player Cold resistance
        Conductive,    // 14 Penetrates player Lightning resistance
        Sundering,     // 15 Penetrates player effective defense (% subtraction before physRes)
        ChaosInfused,       // 16 Deal Chaos elemental damage
        ChaosResistant,     // 17 Increased Chaos resistance
        ChaosPenetrating,   // 18 Penetrates player Chaos resistance
    }

    public struct EnemyModifier
    {
        public ModifierType modifierType = ModifierType.None;
        public int magnitude = 0;

        public EnemyModifier(ModifierType _modifierType, int _magnitude)
        {
            this.modifierType = _modifierType;
            this.magnitude = _magnitude;
        }

        public EnemyModifier(List<int> excludeList, int tier = 0)
        {
            // Indicate if a prefix or suffix is being generated
            GenerateModifier(modifierType, excludeList, tier);
        }

        public void GenerateModifier(ModifierType type, List<int> excludeList, int tier = 0)
        {
            List<int> IDs = new List<int>();
            Random random = new Random();

            IDs.AddRange(Enumerable.Range(1, Enum.GetNames(typeof(ModifierType)).Length - 1));
            // Exclude modifiers already on the NPC and any not present in TierDatabase (disabled modifiers)
            IDs = IDs.Where(val => !excludeList.Contains(val) && TierDatabase.modifierTierDatabase.ContainsKey((ModifierType)val)).ToList();
            // Generate random prefix
            modifierType = (ModifierType)IDs[random.Next(0, IDs.Count)];
            // Get magnitude based on tier
            magnitude = random.Next(TierDatabase.modifierTierDatabase[modifierType][tier].minValue, TierDatabase.modifierTierDatabase[modifierType][tier].maxValue + 1);
        }

    }
}
