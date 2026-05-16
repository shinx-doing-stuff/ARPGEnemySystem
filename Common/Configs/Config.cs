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

        [Range(1.0f, 2.0f)]
        [Increment(0.01f)]
        [DrawTicks]
        [DefaultValue(1.14f)]
        public float ScalingExponent;

        [Range(1.0f, 2.0f)]
        [Increment(0.01f)]
        [DrawTicks]
        [DefaultValue(1.15f)]
        public float DefScalingExponent;

        [Range(0.0f, 1.0f)]
        [Increment(0.01f)]
        [DrawTicks]
        [DefaultValue(0.7f)]
        public float DefenseFloor;

        [Header("Elemental")]

        [Range(1, 200)]
        [DefaultValue(60)]
        public int PhysResHalfPoint;

    }

    public class ConfigClient : ModConfig
    {

        public override ConfigScope Mode => ConfigScope.ClientSide;

        [DefaultValue(false)]
        public bool EnableEnemyStatPanel;

        [DefaultValue(false)]
        public bool EnableElementalDamageLog;
    }
}
