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
        public static readonly float[] PhaseRates    = { 0.003f, 0.006f, 0.010f, 0.015f };
        public static readonly float[] DefPhaseRates = { 0.004f, 0.008f, 0.013f, 0.020f };

        public static float HpDamageMultiplier(int level, int phase, float exponent)
            => 1f + MathF.Pow(level, exponent) * PhaseRates[phase];

        public static float DefenseMultiplier(int level, int phase, float exponent)
            => 1f + MathF.Pow(level, exponent) * DefPhaseRates[phase];

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
