namespace ARPGEnemySystem.Common.Scaling
{
    // Config's [DefaultValue(...)] attributes reference these — single source of truth
    // for the game-design defaults, independent of the server-tunable Config fields.
    public static class ScalingDefaults
    {
        public const float ScalingExponent = 1.14f;
        public const float DefScalingExponent = 1.15f;
        public const float DefenseFloor = 0.7f;
        public const int PhysResHalfPoint = 60;
    }
}
