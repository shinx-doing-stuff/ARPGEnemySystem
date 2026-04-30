using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria.ModLoader.Config;

namespace ARPGEnemySystem.Common.Configs
{
    public class Config : ModConfig
    {

        public override ConfigScope Mode => ConfigScope.ServerSide;

        [DefaultValue(10)]
        public int LevelCapIncreasePerBossDowned;

        [Header("Scaling")]

        [DefaultValue(true)]
        public bool ModifierAllowed;

        [Range(0.5f, 3.0f)]
        [Increment(0.1f)]
        [DrawTicks]
        [DefaultValue(1.3f)]
        public float ScalingExponent;

        [Header("Elemental")]

        [Range(1, 100)]
        [DefaultValue(75)]
        public int ElementalResistanceCap;

        [Range(0, 100)]
        [DefaultValue(67)]
        public int EnemyElementalChance;

        [Range(0, 100)]
        [DefaultValue(25)]
        public int EnemyBaseElementalAllocationPct;

        [Range(0.000f, 2.0f)]
        [Increment(0.05f)]
        [DrawTicks]
        [DefaultValue(0.5f)]
        public float DefenseToPhysResRatio;

        [Range(0.000f, 1.0f)]
        [Increment(0.005f)]
        [DrawTicks]
        [DefaultValue(0.005f)]
        public float EnemyElemResPerLevel;
    }

    public class ConfigClient : ModConfig
    {

        public override ConfigScope Mode => ConfigScope.ClientSide;

        [DefaultValue(true)]
        public bool EnableEnemyStatPanel;

        [DefaultValue(false)]
        public bool EnableElementalDamageLog;
    }
}
