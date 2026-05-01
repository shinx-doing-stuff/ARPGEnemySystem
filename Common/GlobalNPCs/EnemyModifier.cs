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
        Quick,         // 5  Increased Speed
        SoulDrinker,   // 6  Burn Mana
        Flaming,       // 7  Deal Fire elemental damage
        Glacial,       // 8  Deal Cold elemental damage
        Charged,       // 9  Deal Lightning elemental damage
        FireResistant,       // 10 Increased Fire Resistance
        ColdResistant,       // 11 Increased Cold Resistance
        LightningResistant,  // 12 Increased Lightning Resistance
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
            // Exclude modifiers that already on the item
            IDs = IDs.Where(val => !excludeList.Contains(val)).ToList();
            // Generate random prefix
            modifierType = (ModifierType)IDs[random.Next(0, IDs.Count)];
            // Get magnitude based on tier
            magnitude = random.Next(TierDatabase.modifierTierDatabase[modifierType][tier].minValue, TierDatabase.modifierTierDatabase[modifierType][tier].maxValue + 1);
        }

    }
}
