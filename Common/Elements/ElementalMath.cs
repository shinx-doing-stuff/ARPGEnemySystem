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

        // The fraction of a hit that lands on an enemy with this net armour (defense or an element's rating, less the
        // attacker's penetration): halves every `halving` points above zero. Below zero it rises toward the ceiling,
        // halving the gap to it every 2 x `halving` points, so the slope is continuous at zero when the ceiling is 3
        // and over-penetration has diminishing returns.
        public static float ArmourLands(float netArmour, float halving, float vulnerabilityCeiling)
            => netArmour >= 0f
                ? MathF.Pow(2f, -netArmour / halving)
                : vulnerabilityCeiling - (vulnerabilityCeiling - 1f) * MathF.Pow(2f, netArmour / (2f * halving));

        public static float ArmourResistancePct(float netArmour, float halving, float vulnerabilityCeiling)
            => 100f * (1f - ArmourLands(netArmour, halving, vulnerabilityCeiling));
    }
}
