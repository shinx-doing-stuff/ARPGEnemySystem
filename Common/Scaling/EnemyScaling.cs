using ARPGEnemySystem.Common.Elements;
using ARPGEnemySystem.Common.GlobalNPCs;
using System;

namespace ARPGEnemySystem.Common.Scaling
{
    // Plain data carried between the mod's NPC/GlobalNPC fields and the pure functions
    // below — lets EnemyScaling stay free of NPC/GlobalNPC entirely.
    public struct EnemyStatBlock
    {
        public int LifeMax, Damage, Defense;
        public float Scale;
        public float FireDamagePct, ColdDamagePct, LightningDamagePct, ChaosDamagePct;
        public float FireResistance, ColdResistance, LightningResistance, ChaosResistance;
        public float FirePen, ColdPen, LightningPen, ChaosPen;
        public float SunderingPct;
    }

    public static class EnemyScaling
    {
        public static void ApplyLevelScaling(ref EnemyStatBlock s, int level, int phase, float scalingExponent, float defScalingExponent, float defenseFloor)
            => ApplyLevelScaling(ref s, level,
                ScalingMath.HpDamageMultiplier(level, phase, scalingExponent),
                ScalingMath.DefenseMultiplier(level, phase, defScalingExponent),
                defenseFloor);

        public static void ApplyLevelScaling(ref EnemyStatBlock s, int level, float multiplier, float defMultiplier, float defenseFloor)
        {
            // Additive floor ensures low-defense enemies (zombies, slimes) get baseline physRes
            // while preserving relative differences between enemy types.
            s.LifeMax = (int)(s.LifeMax * multiplier);
            s.Damage  = (int)(s.Damage  * multiplier);
            s.Defense += (int)(level * defenseFloor);
            s.Defense  = (int)(s.Defense * defMultiplier);
        }

        public static void ApplyRarityStats(ref EnemyStatBlock s, Rarity rarity)
        {
            var mag = RarityDatabase.rarityModifierDatabase[rarity];
            s.LifeMax += (int)(s.LifeMax * mag[0] / 100f);
            s.Defense += (int)(s.Defense * mag[1] / 100f);
            s.Damage  += (int)(s.Damage  * mag[2] / 100f);
        }

        public static void ApplyRarityElementals(ref EnemyStatBlock s, Rarity rarity)
        {
            int rarityRes = RarityDatabase.rarityElementalResDatabase[rarity];
            s.FireResistance      = rarityRes;
            s.ColdResistance      = rarityRes;
            s.LightningResistance = rarityRes;

            int rarityPen = RarityDatabase.rarityElementalPenDatabase[rarity];
            s.FirePen      = rarityPen;
            s.ColdPen      = rarityPen;
            s.LightningPen = rarityPen;
            s.SunderingPct = rarityPen;

            s.ChaosResistance = RarityDatabase.rarityChaosResDatabase[rarity];
            s.ChaosPen        = RarityDatabase.rarityChaosPenDatabase[rarity];
        }

        public static void ApplyModifier(ref EnemyStatBlock s, ModifierType type, int magnitude)
        {
            switch (type)
            {
                case ModifierType.Colossal:
                    s.Scale = 1 + magnitude / 100f;
                    s.LifeMax += (int)(s.LifeMax * magnitude / 150f);
                    break;
                case ModifierType.Tiny:
                    s.Scale = 1 - magnitude / 100f;
                    s.LifeMax -= (int)(s.LifeMax * magnitude / 200f);
                    break;
                case ModifierType.Strong:
                    s.Damage += (int)(s.Damage * magnitude / 100f);
                    break;
                case ModifierType.Durable:
                    s.Defense += (int)(s.Defense * magnitude / 100f);
                    break;
                case ModifierType.Flaming:
                    s.FireDamagePct += magnitude;
                    break;
                case ModifierType.Glacial:
                    s.ColdDamagePct += magnitude;
                    break;
                case ModifierType.Charged:
                    s.LightningDamagePct += magnitude;
                    break;
                case ModifierType.FireResistant:
                    s.FireResistance += magnitude;
                    break;
                case ModifierType.ColdResistant:
                    s.ColdResistance += magnitude;
                    break;
                case ModifierType.LightningResistant:
                    s.LightningResistance += magnitude;
                    break;
                case ModifierType.Searing:
                    s.FirePen += magnitude;
                    break;
                case ModifierType.Shattering:
                    s.ColdPen += magnitude;
                    break;
                case ModifierType.Conductive:
                    s.LightningPen += magnitude;
                    break;
                case ModifierType.Sundering:
                    s.SunderingPct += magnitude;
                    break;
                case ModifierType.ChaosInfused:
                    s.ChaosDamagePct += magnitude;
                    break;
                case ModifierType.ChaosResistant:
                    s.ChaosResistance += magnitude;
                    break;
                case ModifierType.ChaosPenetrating:
                    s.ChaosPen += magnitude;
                    break;
                case ModifierType.SoulDrinker:
                    // Mana burn — applied in EnemyProfileNPC/ProjectileManager.OnHitPlayer, not here.
                    break;
            }
        }

        // Scaling phase (0-3) to boss package tier (0-2).
        public static int BossTierForPhase(int phase) => phase >= 3 ? 2 : phase >= 1 ? 1 : 0;

        // All three elemental damage types simultaneously (was: single random element).
        // Damage values reduced from {25, 50, 75} -> {10, 12, 14}: the package leans physical.
        // Penetration {5, 10, 15} is separate from Sundering {15, 30, 45}.
        // Chaos values: damage ~40%, res ~25%, pen ~55% of F/C/L.
        public static void ApplyBossElementals(ref EnemyStatBlock s, int tier)
        {
            var cap = ElementalMath.ElementCap;

            float[] elemResValues = { 25f, 50f, 75f };
            float[] damageValues  = { 10f, 12f, 14f };
            float[] penValues     = { 5f,  10f, 15f };
            float[] sunderingValues = { 15f, 30f, 45f };

            s.FireResistance      = Math.Min(elemResValues[tier], cap);
            s.ColdResistance      = Math.Min(elemResValues[tier], cap);
            s.LightningResistance = Math.Min(elemResValues[tier], cap);

            s.FireDamagePct      = damageValues[tier];
            s.ColdDamagePct      = damageValues[tier];
            s.LightningDamagePct = damageValues[tier];

            s.FirePen      = penValues[tier];
            s.ColdPen      = penValues[tier];
            s.LightningPen = penValues[tier];
            s.SunderingPct = sunderingValues[tier];

            float[] chaosResValues    = { 6f,  13f, 19f };
            float[] chaosDamageValues = { 4f,  5f,  6f };
            float[] chaosPenValues    = { 3f,  5f,  8f };

            s.ChaosResistance = Math.Min(chaosResValues[tier], cap);
            s.ChaosDamagePct  = chaosDamageValues[tier];
            s.ChaosPen        = chaosPenValues[tier];
        }
    }
}
