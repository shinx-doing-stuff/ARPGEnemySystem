using System;

namespace ARPGEnemySystem.Common.Elements
{
    public static class ElementalMath
    {
        // Global resistance caps (game-design values, not exposed to players).
        // Player-side max-resistance class stats add ON TOP of these baselines per-player.
        public const float ElementCap = 75f;
        public const float PlayerPhysCap = 80f;

        // Clamps resistance to (-inf, cap]. Negative values allowed (vulnerability).
        public static float ClampResistance(float raw, float cap)
        {
            if (cap <= 0f) return 0f;
            return Math.Min(raw, cap);
        }

        // Returns damage after resistance reduction. resistancePct is a % value (e.g. 30 = 30%).
        public static float ApplyResistance(float damage, float resistancePct, float cap)
            => damage * (1f - ClampResistance(resistancePct, cap) / 100f);

        // cap × d / (|d| + halfPoint), mirrored below zero so negative defense converts to a
        // vulnerability instead of hitting the pole.
        public static float ConvertDefenseToResistance(float defense, float halfPoint, float cap)
            => MathF.Sign(defense) * cap * MathF.Abs(defense) / (MathF.Abs(defense) + halfPoint);
    }
}
