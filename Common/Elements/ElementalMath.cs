using System;

namespace ARPGEnemySystem.Common.Elements
{
    public static class ElementalMath
    {
        // Clamps resistance to (-inf, cap]. Negative values allowed (vulnerability).
        public static float ClampResistance(float raw, float cap)
        {
            if (cap <= 0f) return 0f;
            return Math.Min(raw, cap);
        }

        // Returns damage after resistance reduction. resistancePct is a % value (e.g. 30 = 30%).
        public static float ApplyResistance(float damage, float resistancePct, float cap)
            => damage * (1f - ClampResistance(resistancePct, cap) / 100f);

        // Converts vanilla defense stat to physical resistance %.
        // Formula: min(defense × ratio, cap)
        // Example with ratio=0.5, cap=75: 100 defense → 50% physRes, 150 defense → 75% (cap)
        public static float ConvertDefenseToResistance(float defense, float ratio, float cap)
            => ClampResistance(defense * ratio, cap);
    }
}
