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
        public float FireResistBonusPct, ColdResistBonusPct, LightningResistBonusPct, ChaosResistBonusPct;
        public float PhysCapPct = ScalingDefaults.EnemyPhysCapPct;
        public float FireCapPct = ScalingDefaults.EnemyElementCapPct;
        public float ColdCapPct = ScalingDefaults.EnemyElementCapPct;
        public float LightningCapPct = ScalingDefaults.EnemyElementCapPct;
        public float ChaosCapPct = ScalingDefaults.EnemyChaosCapPct;
        public float FirePen, ColdPen, LightningPen, ChaosPen;
        public float SunderingPct;
        public float ShockThresholdFraction;

        public EnemyStatBlock() { }

        public float ResistBonusPct(Element e) => e switch
        {
            Element.Fire      => FireResistBonusPct,
            Element.Cold      => ColdResistBonusPct,
            Element.Lightning => LightningResistBonusPct,
            Element.Chaos     => ChaosResistBonusPct,
            _                 => 0f,
        };

        public float CapPct(Element e) => e switch
        {
            Element.Physical  => PhysCapPct,
            Element.Fire      => FireCapPct,
            Element.Cold      => ColdCapPct,
            Element.Lightning => LightningCapPct,
            Element.Chaos     => ChaosCapPct,
            _                 => 0f,
        };
    }

    public static class EnemyScaling
    {
        // The rarity and modifier bonuses as fractions to add to 1; EnemyProfile multiplies them in with the level multiplier.
        public static void AddStatBonus(Rarity rarity, EnemyModifier[] modifiers, out float life, out float defense, out float damage)
        {
            life = defense = damage = 0f;
            if (rarity != Rarity.None)
            {
                var mag = RarityDatabase.rarityModifierDatabase[rarity];
                life    += mag[0] / 100f;
                defense += mag[1] / 100f;
                damage  += mag[2] / 100f;
            }
            foreach (var m in modifiers)
            {
                switch (m.modifierType)
                {
                    case ModifierType.Colossal: life    += m.magnitude / 150f; break;
                    case ModifierType.Tiny:     life    -= m.magnitude / 200f; break;
                    case ModifierType.Strong:   damage  += m.magnitude / 100f; break;
                    case ModifierType.Durable:  defense += m.magnitude / 100f; break;
                }
            }
        }

        public static void ApplyRarityElementals(ref EnemyStatBlock s, Rarity rarity)
        {
            int elementalRes = RarityDatabase.rarityElementalResistancePct[rarity];
            s.FireResistBonusPct      = elementalRes;
            s.ColdResistBonusPct      = elementalRes;
            s.LightningResistBonusPct = elementalRes;
            s.ChaosResistBonusPct     = RarityDatabase.rarityChaosResistancePct[rarity];
            s.ShockThresholdFraction = 1f;

            int rarityPen = RarityDatabase.rarityElementalPenDatabase[rarity];
            s.FirePen      = rarityPen;
            s.ColdPen      = rarityPen;
            s.LightningPen = rarityPen;
            s.SunderingPct = rarityPen;

            s.ChaosPen = RarityDatabase.rarityChaosPenDatabase[rarity];
        }

        // Life, defense and damage bonuses are in AddStatBonus; this keeps the scale and the package.
        public static void ApplyModifier(ref EnemyStatBlock s, ModifierType type, int magnitude)
        {
            switch (type)
            {
                case ModifierType.Colossal:
                    s.Scale = 1 + magnitude / 100f;
                    break;
                case ModifierType.Tiny:
                    s.Scale = 1 - magnitude / 100f;
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
                    s.FireResistBonusPct += magnitude;
                    break;
                case ModifierType.ColdResistant:
                    s.ColdResistBonusPct += magnitude;
                    break;
                case ModifierType.LightningResistant:
                    s.LightningResistBonusPct += magnitude;
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
                    s.ChaosResistBonusPct += magnitude;
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
        // Chaos values: damage ~40%, pen ~55% of F/C/L. Resistance is not set here: a boss's elemental
        // ratings follow its defense, and of the resistance terms the package carries only the caps and the Shock threshold fraction.
        public static void ApplyBossElementals(ref EnemyStatBlock s, int tier)
        {
            float[] shockThresholdFractions = { 1f, 0.025f, 0.0125f };
            float[] damageValues  = { 10f, 12f, 14f };
            float[] penValues     = { 5f,  10f, 15f };
            float[] sunderingValues = { 15f, 30f, 45f };

            s.ShockThresholdFraction = shockThresholdFractions[tier];

            s.FireDamagePct      = damageValues[tier];
            s.ColdDamagePct      = damageValues[tier];
            s.LightningDamagePct = damageValues[tier];

            s.FirePen      = penValues[tier];
            s.ColdPen      = penValues[tier];
            s.LightningPen = penValues[tier];
            s.SunderingPct = sunderingValues[tier];

            float[] chaosDamageValues = { 4f,  5f,  6f };
            float[] chaosPenValues    = { 3f,  5f,  8f };

            s.ChaosDamagePct  = chaosDamageValues[tier];
            s.ChaosPen        = chaosPenValues[tier];
        }

        // An element's rating before the law: the element's share of the enemy's defense.
        public static float ElementalArmour(int defense, Element element)
        {
            float ratio = element == Element.Chaos ? ScalingDefaults.ChaosArmourRatio : ScalingDefaults.ElementalArmourRatio;
            return defense * ratio;
        }

        public static float RawResistancePct(int defense, Element element, float bonusPct, float halving, float vulnerabilityCeiling)
            => ElementalMath.ArmourResistancePct(ElementalArmour(defense, element), halving, vulnerabilityCeiling) + bonusPct;
    }
}
