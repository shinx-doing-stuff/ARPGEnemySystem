using ARPGEnemySystem.Common.Scaling;
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

        [Header("Scaling")]

        [Range(1.0f, 2.0f)]
        [Increment(0.01f)]
        [DrawTicks]
        [DefaultValue(ScalingDefaults.ScalingExponent)]
        public float ScalingExponent;

        [Range(1.0f, 2.0f)]
        [Increment(0.01f)]
        [DrawTicks]
        [DefaultValue(ScalingDefaults.DefScalingExponent)]
        public float DefScalingExponent;

        [Range(0.0f, 1.0f)]
        [Increment(0.01f)]
        [DrawTicks]
        [DefaultValue(ScalingDefaults.DefenseFloor)]
        public float DefenseFloor;

        [Header("Elemental")]

        [Range(1, 200)]
        [DefaultValue(ScalingDefaults.PhysResHalfPoint)]
        public int PhysResHalfPoint;

        [Header("Debug")]

        [DefaultValue(false)]
        public bool EnableBossScalingLog;

    }

    public class ConfigClient : ModConfig
    {

        public override ConfigScope Mode => ConfigScope.ClientSide;

        [DefaultValue(false)]
        public bool EnableEnemyStatPanel;

        [DefaultValue(false)]
        public bool EnableElementalDamageLog;

        [DefaultValue(false)]
        public bool EnableReapLog;
    }
}
