using System;
using ARPGEnemySystem.Common.GlobalNPCs;

namespace ARPGEnemySystem.Common.Scaling
{
    public enum EnemyKind : byte
    {
        Regular = 0,
        FightMember = 1,
    }

    // Server-synced config inputs, so every machine derives the same numbers.
    public readonly record struct ScalingSettings(float ScalingExponent, float DefScalingExponent, float DefenseFloor);

    // One enemy's scaling, rolled once on the server. Immutable, so a fight, its projectiles and
    // SetDefaults re-runs share one instance.
    public sealed class EnemyProfile
    {
        public readonly EnemyKind Kind;
        public readonly int Level;
        public readonly int Phase;
        public readonly Rarity Rarity;
        public readonly EnemyModifier[] Modifiers;

        // Only the elemental fields are used.
        public readonly EnemyStatBlock Package;

        // Set by Colossal/Tiny; null keeps the NPC's own scale.
        public readonly float? ScaleOverride;

        private readonly float _multiplier;
        private readonly float _defMultiplier;
        private readonly float _defenseFloor;

        public EnemyProfile(EnemyKind kind, int level, int phase, Rarity rarity, EnemyModifier[] modifiers, ScalingSettings settings)
        {
            Kind = kind;
            Level = level;
            Phase = phase;
            // A boss-fight member takes the boss package only: no rarity, no modifiers.
            Rarity = kind == EnemyKind.FightMember ? Rarity.None : rarity;
            Modifiers = kind == EnemyKind.FightMember ? Array.Empty<EnemyModifier>() : modifiers;

            _multiplier = ScalingMath.HpDamageMultiplier(level, phase, settings.ScalingExponent);
            _defMultiplier = ScalingMath.DefenseMultiplier(level, phase, settings.DefScalingExponent);
            _defenseFloor = settings.DefenseFloor;

            var package = new EnemyStatBlock();
            if (kind == EnemyKind.FightMember)
                EnemyScaling.ApplyBossElementals(ref package, EnemyScaling.BossTierForPhase(phase));
            else
                EnemyScaling.ApplyRarityElementals(ref package, Rarity);

            foreach (var m in Modifiers)
            {
                EnemyScaling.ApplyModifier(ref package, m.modifierType, m.magnitude);
                if (m.modifierType == ModifierType.Colossal || m.modifierType == ModifierType.Tiny)
                    ScaleOverride = package.Scale;
            }
            Package = package;
        }

        public bool SameInputs(EnemyKind kind, int level, int phase, Rarity rarity, ReadOnlySpan<EnemyModifier> modifiers)
        {
            if (Kind != kind || Level != level || Phase != phase || Rarity != rarity || Modifiers.Length != modifiers.Length)
                return false;
            for (int i = 0; i < Modifiers.Length; i++)
            {
                if (Modifiers[i].modifierType != modifiers[i].modifierType || Modifiers[i].magnitude != modifiers[i].magnitude)
                    return false;
            }
            return true;
        }

        // Level, then rarity, then modifiers, truncating at each stage.
        public void ApplyStats(ref EnemyStatBlock s)
        {
            EnemyScaling.ApplyLevelScaling(ref s, Level, _multiplier, _defMultiplier, _defenseFloor);
            if (Rarity != Rarity.None)
                EnemyScaling.ApplyRarityStats(ref s, Rarity);
            foreach (var m in Modifiers)
                EnemyScaling.ApplyModifier(ref s, m.modifierType, m.magnitude);
        }

        public int ScaleDamage(int raw)
        {
            var s = new EnemyStatBlock { Damage = raw };
            ApplyStats(ref s);
            return s.Damage;
        }

        public int ScaleDefense(int raw)
        {
            var s = new EnemyStatBlock { Defense = raw };
            ApplyStats(ref s);
            return s.Defense;
        }

        public int ScaleLifeMax(int raw)
        {
            var s = new EnemyStatBlock { LifeMax = raw };
            ApplyStats(ref s);
            return s.LifeMax;
        }
    }
}
