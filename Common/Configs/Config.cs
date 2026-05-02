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
        [DefaultValue(1.13f)]
        public float ScalingExponent;

        [Range(0.5f, 3.0f)]
        [Increment(0.1f)]
        [DrawTicks]
        [DefaultValue(1.2f)]
        public float DefScalingExponent;

        [Range(0.0f, 1.0f)]
        [Increment(0.05f)]
        [DrawTicks]
        [DefaultValue(0.15f)]
        public float DefenseFloor;

        [Header("Elemental")]

        [Range(1, 100)]
        [DefaultValue(75)]
        public int ElementalResistanceCap;

        [Range(1, 100)]
        [DefaultValue(80)]
        public int PlayerPhysResCap;

        [Range(1, 200)]
        [DefaultValue(30)]
        public int PhysResHalfPoint;

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
