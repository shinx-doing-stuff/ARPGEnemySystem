using System;
using Terraria;

namespace ARPGEnemySystem.Common.Scaling
{
    // Pure world-level and stat-multiplier math, moved out of WorldManager so it can be
    // exercised without a loaded world (BalanceLab links this file directly).
    public static class ScalingMath
    {
        public const int BaseLevel = 10;
        public const int MaxLevel = 200;

        // Hardcoded — these are game design values, not server-tuning knobs.
        // Phase 0 = pre-hardmode, 1 = post-WoF, 2 = post-all-mechs, 3 = post-Plantera.
        // Each phase's slope applies only to the level range above its start level, so the curve has no jump.
        public static readonly int[] PhaseStartLevels = { 0, 94, 137, 147 };

        // Per-kind tables, indexed by (int)EnemyKind: Regular, FightMember.
        public static readonly float[] HpBase = { 1f, 1.83f };
        public static readonly float[][] HpRates =
        {
            new[] { 0.003f, 0.006f, 0.010f, 0.015f },
            new[] { 0.00022f, 0.0106f, 0.0397f, 0.0313f },
        };

        public static readonly float[] DamageBase = { 1f, 0.396f };
        public static readonly float[][] DamageRates =
        {
            new[] { 0.003f, 0.006f, 0.010f, 0.015f },
            new[] { 0.00725f, 0.00039f, 0f, 0.00446f },
        };

        // mult = base + sum over phases k up to the current phase of rate_k x (the part of level^exponent inside phase k's range).
        public static float PiecewiseMultiplier(int level, int phase, float exponent, float baseValue, float[] rates)
        {
            float x = MathF.Pow(level, exponent);
            float result = baseValue;
            for (int k = 0; k <= phase; k++)
            {
                float from = MathF.Pow(PhaseStartLevels[k], exponent);
                float to = k < phase ? MathF.Min(x, MathF.Pow(PhaseStartLevels[k + 1], exponent)) : x;
                result += rates[k] * MathF.Max(0f, to - from);
            }
            return result;
        }

        public static float HpMultiplier(int level, int phase, float exponent, EnemyKind kind)
            => PiecewiseMultiplier(level, phase, exponent, HpBase[(int)kind], HpRates[(int)kind]);

        public static float DamageMultiplier(int level, int phase, float exponent, EnemyKind kind)
            => PiecewiseMultiplier(level, phase, exponent, DamageBase[(int)kind], DamageRates[(int)kind]);

        public static float LevelsPerBoss(int rosterCount)
        {
            if (rosterCount == 0)
                return 0f;

            return (float)(MaxLevel - BaseLevel) / rosterCount;
        }

        public static int LevelCap(int rosterCount, int downedCount)
            => BaseLevel + (int)MathF.Round(LevelsPerBoss(rosterCount) * downedCount);

        public static int GetScalingPhase()
        {
            if (NPC.downedPlantBoss)                                               return 3;
            if (NPC.downedMechBoss1 && NPC.downedMechBoss2 && NPC.downedMechBoss3) return 2;
            if (Main.hardMode)                                                     return 1;
            return 0;
        }
    }
}
