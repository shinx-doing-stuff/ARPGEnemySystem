namespace ARPGEnemySystem.Common.Scaling
{
    // Config's [DefaultValue(...)] attributes reference these — single source of truth
    // for the game-design defaults, independent of the server-tunable Config fields.
    public static class ScalingDefaults
    {
        public const float ScalingExponent = 1.14f;
        public const int EnemyPhysResHalfPoint = 25;
        public const float VanillaArmorPenScale = 1f / 3f;
        public const int PhysResHalfPoint = 60;
    }
}
