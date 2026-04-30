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
        // Formula: cap × defense / (defense + halfPoint)
        // halfPoint is the defense value at which physRes = cap / 2.
        // Example with halfPoint=30, cap=75: 30 defense → 37.5%, 60 defense → 50%, 200 defense → 57.7%
        // The result is always strictly less than cap for any finite defense value.
        public static float ConvertDefenseToResistance(float defense, float halfPoint, float cap)
            => cap * defense / (defense + halfPoint);
    }
}
