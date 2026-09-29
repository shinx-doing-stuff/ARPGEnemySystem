# Enemy Scaling Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make enemy level scaling impossible to lose. Damage and defense are scaled at the moment they are read, from one immutable per-enemy profile that survives every `SetDefaults` re-run and syncs to clients in a few bytes.

**Architecture:**
- **Profile (pure, ARPGEnemySystem `Common/Scaling`):** a pure `EnemyProfile` holds level, phase, rarity and modifiers plus precomputed coefficients. `EnemyProfileCodec` puts its inputs on the wire.
- **Owner:** one `GlobalNPC` (`EnemyProfileNPC`) creates the profile at spawn and re-attaches it across `NPC.SetDefaults` re-runs. It writes only max life, value and size, and syncs the profile.
- **Accessor:** the static `EnemyStats` is the only way mod code reads an enemy's damage and defense.
- **ARPGCharacterSystem:** reads through the accessor, and folds vanilla's defense reducers into the mod's resistance.
- **BalanceLab:** links the pure files, tests them, mirrors the new rules, and adds a guard test against raw reads.

**Tech Stack:** C# / .NET 8, tModLoader 1.4.4 (`GlobalNPC`, `GlobalProjectile`, `On_NPC` detours, `BitWriter`/`BinaryWriter` extra-AI sync), xunit in BalanceLab.

**Spec:** `ARPGEnemySystem/docs/superpowers/specs/2026-09-29-enemy-scaling-refactor-design.md`. Read it before any task; rulings R1–R8 are binding.

## Global Constraints

**Repos and git**
- Paths below are relative to `ModSources/`. Repos: `ARPGEnemySystem` (ES), `ARPGCharacterSystem` (CS), `BalanceLab`. `ARPGItemSystem` (IS) is read-only here.
- Never create or switch branches in ES/CS/IS. The user decides the branch before execution. Commit on whatever is checked out.
- Stage only the paths the task names (`git add <path>`, never `-A`). Never `git stash`, `reset` or `checkout --`. Edit files in place with the Edit tool. Never regenerate a file from a stale read.
- ES/CS commits happen only if the user approved committing when approving this plan. If they did not, leave the changes staged and report.

**Build and test**
- Mod build check: run `dotnet build` in the mod folder. `TML003` + `MSB3073` with **0 `error CS`** lines means the code compiled.
- The mods have no test harness. Pure code is tested through BalanceLab's compile-links: `dotnet test` in `BalanceLab/`.
- Known flake: `ProgressionFlagsTests.AllFlagsSet_TierFunctionsStayInRange_AndPhaseIs3` (~5%, ES `Rarity.cs`). If exactly that test fails, re-run.

**Code style**
- Plain explicit C#. Sparse comments that match the surrounding density: only constraints the code can't show, plus `File.cs:line` citations for every vanilla port. No "task N"/"fix round" history in code or tests.
- Every code-writing subagent runs on model `sonnet`.

**Game-facing rules**
- Multiplayer: clients never roll levels, rarity or modifiers. Everything a client needs arrives in extra-AI.
- `ModifierType`, `Rarity` and `AffixId` enum values are persisted or synced as numbers. Never reorder them.
- Localization: reuse the existing `Mods.ARPGEnemySystem.NPCTooltip.*` keys. Add none.
- Spec rulings, verbatim:
  - **R1:** "Scale at read time. Damage and defense are never written with scaled values."
  - **R7:** mirrored conversion `sign(d) · cap · |d| / (|d| + halfPoint)`.
  - **R8:** "No resistance floor anywhere, in either direction."

## Review Focus

These are failure modes the spec implies but unit tests can't reach, because they live in tModLoader hooks. Each has an in-game check in the task that owns the code, and reviewers must trace each one in the code.

1. **Natural-spawn variants and negative netIDs.** Vanilla's spawner calls `SetDefaults(variant)` right after `NewNPC` (NPC.cs:88766-89251), and a negative netID re-enters `SetDefaults`. Max life must be scaled **exactly once**, and the profile must be the one rolled in `OnSpawn`. Owned by Task 3, step 9.
2. **First sight of a full-HP enemy on a client.** Vanilla sets `life = lifeMax` before mod data is read (MessageBuffer.cs:1843-1845). The client must end at the **scaled** full life. Owned by Task 3, step 9.
3. **Client slot reuse.** A new enemy arriving in a recycled NPC slot must scale from its own vanilla base, never on top of the previous occupant's scaled max life. Owned by Task 3, step 9.
4. **Negative defense with percentage armor pen.** Percentage pen must never make a negative defense less negative. Owned by Task 4 (code) and Task 6 (BalanceLab test `NegativeDefense_PercentPenLeavesVulnerabilityUntouched`).
5. **Projectile chains.** A projectile or NPC spawned by an enemy projectile inherits the fight profile (Sharknado → Sharkron, split shots). Traps and boulders stay plain vanilla. Owned by Task 3, step 9.

## File Map

| File | Change | Responsibility |
|---|---|---|
| `ARPGEnemySystem/Common/Scaling/EnemyProfile.cs` | create | Pure immutable profile: inputs, coefficients, package, `Scale*` |
| `ARPGEnemySystem/Common/Scaling/EnemyProfileCodec.cs` | create | Pure wire format of the profile inputs |
| `ARPGEnemySystem/Common/Scaling/EnemyScaling.cs` | modify | `ApplyLevelScaling` overload with precomputed multipliers; `BossTierForPhase`; remove `BossElementalTier` |
| `ARPGEnemySystem/Common/Elements/ElementalMath.cs` | modify | Mirrored defense conversion |
| `ARPGEnemySystem/Common/GlobalNPCs/EnemyProfileNPC.cs` | create | Profile owner: spawn creation, SetDefaults detour, writes, sync, defense step, Soul Drinker |
| `ARPGEnemySystem/Common/GlobalNPCs/EnemyStats.cs` | create | The accessor |
| `ARPGEnemySystem/Common/GlobalNPCs/NPCManager.cs`, `BossManager.cs` | delete | Replaced by `EnemyProfileNPC` |
| `ARPGEnemySystem/Common/GlobalProjectiles/ProjectileManager.cs` | modify | Profile reference + shooter slot; compact sync |
| `ARPGEnemySystem/Common/UI/NPCTooltip.cs` | modify | One builder over profile + accessor |
| `ARPGEnemySystem/ARPGEnemySystem.cs` | modify | `GetEnemyInfo` from the profile |
| `ARPGCharacterSystem/Common/Combat/VanillaDefenseReducers.cs` | create | Vanilla's defense reducers, ported whole |
| `ARPGCharacterSystem/Common/Combat/TargetResistances.cs` | modify | Accessor + reducers, no floor |
| `ARPGCharacterSystem/Common/Combat/ElementalDamageCalculator.cs`, `ReapResolver.cs` | modify | Pass reducers |
| `ARPGCharacterSystem/Common/Players/OutgoingHitPlayer.cs` | modify | Build reducers from the hit context |
| `ARPGCharacterSystem/Common/Players/PlayerHurtPipeline.cs` | modify | Profile-based branches, vanilla projectile maths, no floors |
| `ARPGCharacterSystem/Common/GlobalNPCs/XPAwardGlobalNPC.cs` | modify | Accessor + profile |
| `BalanceLab/src/BalanceLab.csproj` | modify | Link the two new pure files |
| `BalanceLab/tests/EnemyProfileTests.cs`, `ElementalMathTests.cs`, `EnemyProfileCodecTests.cs`, `RawEnemyStatReadTests.cs` | create | Tests |
| `BalanceLab/src/Sim/{EnemyState,HitResolver,ReapSim,SurvivalCalc}.cs`, `src/Model/BossData.cs`, `src/EngineConstants.cs`, `data/vanilla/bosses/*.json`, `tests/*`, `tests/mirrored-sources.json`, `docs/ASSUMPTIONS.md`, `CLAUDE.md` | modify | Re-sync to the new model |
| `ARPGEnemySystem/CLAUDE.md`, `ARPGCharacterSystem/docs/systems/*` | modify | Docs |

---

### Task 1: Pure profile core and mirrored defense conversion

**Files:**
- Create: `ARPGEnemySystem/Common/Scaling/EnemyProfile.cs`
- Modify: `ARPGEnemySystem/Common/Scaling/EnemyScaling.cs:22-33` (split `ApplyLevelScaling`), `:128` area (add `BossTierForPhase`)
- Modify: `ARPGEnemySystem/Common/Elements/ElementalMath.cs:23-29`
- Modify: `BalanceLab/src/BalanceLab.csproj` (ES link block, lines 130-139)
- Test: `BalanceLab/tests/EnemyProfileTests.cs`, `BalanceLab/tests/ElementalMathTests.cs`

**Interfaces:**
- Produces:
  - `enum EnemyKind : byte { Regular = 0, FightMember = 1 }`
  - `readonly record struct ScalingSettings(float ScalingExponent, float DefScalingExponent, float DefenseFloor)`
  - `sealed class EnemyProfile`:
    - read-only fields `Kind`, `Level`, `Phase`, `Rarity`, `EnemyModifier[] Modifiers`, `EnemyStatBlock Package`, `float? ScaleOverride`;
    - ctor `EnemyProfile(EnemyKind kind, int level, int phase, Rarity rarity, EnemyModifier[] modifiers, ScalingSettings settings)`;
    - `void ApplyStats(ref EnemyStatBlock s)`, `int ScaleDamage(int raw)`, `int ScaleDefense(int raw)`, `int ScaleLifeMax(int raw)`.
  - `EnemyScaling.ApplyLevelScaling(ref EnemyStatBlock s, int level, float multiplier, float defMultiplier, float defenseFloor)`
  - `EnemyScaling.BossTierForPhase(int phase) → int`
  - `ElementalMath.ConvertDefenseToResistance` keeps its signature, with the mirrored body.

- [ ] **Step 1: Write the failing tests**

`BalanceLab/tests/EnemyProfileTests.cs`:

```csharp
using System;
using ARPGEnemySystem.Common.GlobalNPCs;
using ARPGEnemySystem.Common.Scaling;
using Xunit;

public class EnemyProfileTests
{
    static readonly ScalingSettings Defaults = new(ScalingDefaults.ScalingExponent, ScalingDefaults.DefScalingExponent, ScalingDefaults.DefenseFloor);

    // Today's write-once order (NPCManager.PreAI / BossManager.OnSpawn): level, then rarity, then modifiers.
    static EnemyStatBlock Staged(EnemyStatBlock s, int level, int phase, Rarity rarity, EnemyModifier[] mods)
    {
        EnemyScaling.ApplyLevelScaling(ref s, level, phase, ScalingDefaults.ScalingExponent, ScalingDefaults.DefScalingExponent, ScalingDefaults.DefenseFloor);
        if (rarity != Rarity.None) EnemyScaling.ApplyRarityStats(ref s, rarity);
        foreach (var m in mods) EnemyScaling.ApplyModifier(ref s, m.modifierType, m.magnitude);
        return s;
    }

    [Fact]
    public void RegularProfile_ScalesEachStatExactlyLikeTheStagedSequence()
    {
        var mods = new[] { new EnemyModifier(ModifierType.Strong, 20), new EnemyModifier(ModifierType.Durable, 15), new EnemyModifier(ModifierType.Colossal, 30) };
        var p = new EnemyProfile(EnemyKind.Regular, 50, 1, Rarity.Rare, mods, Defaults);
        var expected = Staged(new EnemyStatBlock { LifeMax = 340, Damage = 40, Defense = 18 }, 50, 1, Rarity.Rare, mods);

        Assert.Equal(expected.LifeMax, p.ScaleLifeMax(340));
        Assert.Equal(expected.Damage, p.ScaleDamage(40));
        Assert.Equal(expected.Defense, p.ScaleDefense(18));
    }

    [Fact]
    public void Level0_ChangesNothing()
    {
        // Pow(0, e) == 0 makes both multipliers exactly 1, and the defense floor adds (int)(0 * 0.7) = 0.
        var p = new EnemyProfile(EnemyKind.FightMember, 0, 0, Rarity.None, Array.Empty<EnemyModifier>(), Defaults);
        Assert.Equal(30, p.ScaleDamage(30));
        Assert.Equal(12, p.ScaleDefense(12));
        Assert.Equal(2800, p.ScaleLifeMax(2800));
    }

    [Fact]
    public void Scaling_IsAPureFunctionOfTheRawValue()
    {
        var p = new EnemyProfile(EnemyKind.Regular, 120, 2, Rarity.Elite, new[] { new EnemyModifier(ModifierType.Strong, 30) }, Defaults);
        int first = p.ScaleDamage(55);
        Assert.Equal(first, p.ScaleDamage(55));
        Assert.Equal(first, p.ScaleDamage(55));
    }

    [Fact]
    public void FightMember_DropsRarityAndModifiers_AndTakesTheBossPackageForItsPhase()
    {
        var p = new EnemyProfile(EnemyKind.FightMember, 150, 3, Rarity.Legend, new[] { new EnemyModifier(ModifierType.Strong, 50) }, Defaults);

        Assert.Equal(Rarity.None, p.Rarity);
        Assert.Empty(p.Modifiers);
        // Phase 3 -> boss tier 2: ApplyBossElementals' third column (EnemyScaling.cs).
        Assert.Equal(75f, p.Package.FireResistance);
        Assert.Equal(45f, p.Package.FireDamagePct);
        Assert.Equal(23f, p.Package.ChaosPen);
        var expected = Staged(new EnemyStatBlock { Damage = 70 }, 150, 3, Rarity.None, Array.Empty<EnemyModifier>());
        Assert.Equal(expected.Damage, p.ScaleDamage(70));
    }

    [Fact]
    public void RegularPackage_IsTheRarityBaselinePlusModifiers()
    {
        // Rare: F/C/L res 20, pen 10 (RarityDatabase); Searing adds 12 fire pen.
        var p = new EnemyProfile(EnemyKind.Regular, 40, 0, Rarity.Rare, new[] { new EnemyModifier(ModifierType.Searing, 12) }, Defaults);
        Assert.Equal(20f, p.Package.FireResistance);
        Assert.Equal(22f, p.Package.FirePen);
        Assert.Null(p.ScaleOverride);
    }

    [Fact]
    public void Colossal_SetsTheScaleOverride()
    {
        var p = new EnemyProfile(EnemyKind.Regular, 40, 0, Rarity.Common, new[] { new EnemyModifier(ModifierType.Colossal, 30) }, Defaults);
        Assert.Equal(1.3f, p.ScaleOverride.Value, 3);
    }

    [Fact]
    public void NegativeRawDefense_ScalesThroughTheSameStages()
    {
        // EoC's last phase writes defense = -30 (NPC.cs:20747). Level 23: floor add (int)(23 * 0.7) = 16 -> -14,
        // then (int)(-14 * DefenseMultiplier(23, 0)) truncates toward zero.
        var p = new EnemyProfile(EnemyKind.FightMember, 23, 0, Rarity.None, Array.Empty<EnemyModifier>(), Defaults);
        float defMult = ScalingMath.DefenseMultiplier(23, 0, ScalingDefaults.DefScalingExponent);
        Assert.Equal((int)(-14 * defMult), p.ScaleDefense(-30));
        Assert.True(p.ScaleDefense(-30) < 0);
    }
}
```

`BalanceLab/tests/ElementalMathTests.cs`:

```csharp
using System;
using ARPGEnemySystem.Common.Elements;
using Xunit;

public class ElementalMathTests
{
    [Theory]
    [InlineData(30f, 25f)]      // 75 * 30 / 90
    [InlineData(60f, 37.5f)]    // 75 * 60 / 120
    [InlineData(0f, 0f)]
    [InlineData(-30f, -25f)]    // mirrored
    [InlineData(-60f, -37.5f)]  // no pole at -halfPoint
    public void DefenseConversion_IsMirroredAroundZero(float defense, float expected)
        => Assert.Equal(expected, ElementalMath.ConvertDefenseToResistance(defense, 60f, 75f), 3);

    [Fact]
    public void DefenseConversion_KeepsTheSignAndStaysBelowTheCapInMagnitude()
    {
        for (int d = -1000; d <= 1000; d += 7)
        {
            float r = ElementalMath.ConvertDefenseToResistance(d, 60f, 75f);
            Assert.True(MathF.Abs(r) < 75f);
            Assert.Equal(Math.Sign(d), Math.Sign(r));
        }
    }

    [Fact]
    public void ApplyResistance_HasNoFloor_AndClampsAtTheCap()
    {
        Assert.Equal(150f, ElementalMath.ApplyResistance(100f, -50f, 75f), 3);
        Assert.Equal(25f, ElementalMath.ApplyResistance(100f, 90f, 75f), 3);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run `dotnet test --filter "FullyQualifiedName~EnemyProfileTests|FullyQualifiedName~ElementalMathTests"` in `BalanceLab/`.
Expected: a build error, `The type or namespace name 'EnemyProfile' could not be found`.

- [ ] **Step 3: Split `ApplyLevelScaling` and add `BossTierForPhase`**

In `ARPGEnemySystem/Common/Scaling/EnemyScaling.cs`, replace the whole `ApplyLevelScaling` method (lines 22-33) with:

```csharp
        public static void ApplyLevelScaling(ref EnemyStatBlock s, int level, int phase, float scalingExponent, float defScalingExponent, float defenseFloor)
            => ApplyLevelScaling(ref s, level,
                ScalingMath.HpDamageMultiplier(level, phase, scalingExponent),
                ScalingMath.DefenseMultiplier(level, phase, defScalingExponent),
                defenseFloor);

        // Precomputed-multiplier form: EnemyProfile computes the Pow once and reuses it on every read.
        public static void ApplyLevelScaling(ref EnemyStatBlock s, int level, float multiplier, float defMultiplier, float defenseFloor)
        {
            // Additive floor ensures low-defense enemies (zombies, slimes) get baseline physRes
            // while preserving relative differences between enemy types.
            s.LifeMax = (int)(s.LifeMax * multiplier);
            s.Damage  = (int)(s.Damage  * multiplier);
            s.Defense += (int)(level * defenseFloor);
            s.Defense  = (int)(s.Defense * defMultiplier);
        }
```

Directly above `public static int BossElementalTier()`, add:

```csharp
        // Scaling phase (0 pre-HM, 1 HM, 2 post-mechs, 3 post-Plantera) to the boss package tier
        // (pre-HM / HM / post-Plantera) — the same buckets BossElementalTier reads from world flags.
        public static int BossTierForPhase(int phase) => phase >= 3 ? 2 : phase >= 1 ? 1 : 0;
```

- [ ] **Step 4: Mirror the defense conversion**

In `ARPGEnemySystem/Common/Elements/ElementalMath.cs`, replace lines 23-29 (the comment block and `ConvertDefenseToResistance`) with:

```csharp
        // Converts defense points to physical resistance %: cap × defense / (defense + halfPoint).
        // halfPoint is the defense value at which physRes = cap / 2. Mirrored below zero, so
        // negative defense (armour pen past zero, or an AI writing negative defense) converts
        // with the same shape instead of hitting the pole at -halfPoint.
        // Example with halfPoint=60, cap=75: 30 → 25%, 60 → 37.5%, -30 → -25%.
        // The magnitude always stays below cap.
        public static float ConvertDefenseToResistance(float defense, float halfPoint, float cap)
            => MathF.Sign(defense) * cap * MathF.Abs(defense) / (MathF.Abs(defense) + halfPoint);
```

- [ ] **Step 5: Create `EnemyProfile.cs`**

`ARPGEnemySystem/Common/Scaling/EnemyProfile.cs`:

```csharp
using System;
using ARPGEnemySystem.Common.GlobalNPCs;

namespace ARPGEnemySystem.Common.Scaling
{
    public enum EnemyKind : byte
    {
        Regular = 0,
        FightMember = 1,
    }

    // The server-synced Config knobs a profile is built from, so every machine derives the same numbers.
    public readonly record struct ScalingSettings(float ScalingExponent, float DefScalingExponent, float DefenseFloor);

    // One enemy's scaling, rolled once on the server and never changed afterwards. Immutable so it is
    // shared by reference: every member of a boss fight, every projectile an enemy fires, and the NPC
    // itself across a SetDefaults re-run all point at the same object.
    public sealed class EnemyProfile
    {
        public readonly EnemyKind Kind;
        public readonly int Level;
        public readonly int Phase;
        public readonly Rarity Rarity;
        public readonly EnemyModifier[] Modifiers;

        // Added elemental damage %, resistances, penetration and sundering. Its LifeMax/Damage/Defense/Scale are unused.
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

        // Level, then rarity, then modifiers — the same stages and integer truncation the old
        // write-once NPCManager.PreAI / BossManager.OnSpawn used.
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
```

- [ ] **Step 6: Link the new file in BalanceLab**

In `BalanceLab/src/BalanceLab.csproj`, directly after the `EnemyScaling.cs` link line (line 138), add:

```xml
    <Compile Include="..\..\ARPGEnemySystem\Common\Scaling\EnemyProfile.cs" Link="Linked\ES\EnemyProfile.cs" />
```

- [ ] **Step 7: Run the tests to verify they pass**

Run `dotnet test --filter "FullyQualifiedName~EnemyProfileTests|FullyQualifiedName~ElementalMathTests"` in `BalanceLab/`. Expected: all pass.
Then run `dotnet test`. Expected: everything passes (the known flake aside).

- [ ] **Step 8: Build the mod**

Run `dotnet build` in `ARPGEnemySystem/`. Expected: 0 `error CS`.

- [ ] **Step 9: Commit**

```bash
git -C ARPGEnemySystem add Common/Scaling/EnemyProfile.cs Common/Scaling/EnemyScaling.cs Common/Elements/ElementalMath.cs
git -C ARPGEnemySystem commit -m "feat(scaling): immutable EnemyProfile; mirrored defense-to-resistance conversion"
git -C BalanceLab add src/BalanceLab.csproj tests/EnemyProfileTests.cs tests/ElementalMathTests.cs
git -C BalanceLab commit -m "Link EnemyProfile; pin profile scaling and mirrored conversion"
```

---

### Task 2: Profile wire format

**Files:**
- Create: `ARPGEnemySystem/Common/Scaling/EnemyProfileCodec.cs`
- Modify: `ARPGEnemySystem/Common/Scaling/EnemyProfile.cs` (add `SameInputs`)
- Modify: `BalanceLab/src/BalanceLab.csproj`
- Test: `BalanceLab/tests/EnemyProfileCodecTests.cs`

**Interfaces:**
- Consumes: `EnemyProfile`, `EnemyKind`, `ScalingSettings` (Task 1).
- Produces:
  - `EnemyProfile.SameInputs(EnemyKind kind, int level, int phase, Rarity rarity, ReadOnlySpan<EnemyModifier> modifiers) → bool`
  - `EnemyProfileCodec.Write(BinaryWriter w, EnemyProfile p)`
  - `EnemyProfileCodec.Read(BinaryReader r, EnemyProfile current, ScalingSettings settings) → EnemyProfile`. It returns `current` itself when the inputs match.

- [ ] **Step 1: Write the failing tests**

`BalanceLab/tests/EnemyProfileCodecTests.cs`:

```csharp
using System;
using System.IO;
using ARPGEnemySystem.Common.GlobalNPCs;
using ARPGEnemySystem.Common.Scaling;
using Xunit;

public class EnemyProfileCodecTests
{
    static readonly ScalingSettings Defaults = new(ScalingDefaults.ScalingExponent, ScalingDefaults.DefScalingExponent, ScalingDefaults.DefenseFloor);

    static byte[] Encode(EnemyProfile p)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms)) EnemyProfileCodec.Write(w, p);
        return ms.ToArray();
    }

    static EnemyProfile Decode(byte[] bytes, EnemyProfile current)
    {
        using var r = new BinaryReader(new MemoryStream(bytes));
        return EnemyProfileCodec.Read(r, current, Defaults);
    }

    static EnemyProfile Elite87() => new(EnemyKind.Regular, 87, 2, Rarity.Elite,
        new[] { new EnemyModifier(ModifierType.Strong, 25), new EnemyModifier(ModifierType.Sundering, 40) }, Defaults);

    [Fact]
    public void Regular_RoundTrips()
    {
        var p = Elite87();
        var q = Decode(Encode(p), null);
        Assert.Equal(EnemyKind.Regular, q.Kind);
        Assert.Equal(87, q.Level);
        Assert.Equal(2, q.Phase);
        Assert.Equal(Rarity.Elite, q.Rarity);
        Assert.Equal(p.Modifiers, q.Modifiers);
        Assert.Equal(p.ScaleDamage(50), q.ScaleDamage(50));
        Assert.Equal(p.Package.SunderingPct, q.Package.SunderingPct);
    }

    [Fact]
    public void FightMember_RoundTrips()
    {
        var p = new EnemyProfile(EnemyKind.FightMember, 212, 3, Rarity.None, Array.Empty<EnemyModifier>(), Defaults);
        var q = Decode(Encode(p), null);
        Assert.Equal(EnemyKind.FightMember, q.Kind);
        Assert.Equal(212, q.Level);
        Assert.Equal(3, q.Phase);
        Assert.Empty(q.Modifiers);
    }

    [Fact]
    public void Read_ReturnsTheCurrentInstance_WhenTheInputsMatch()
    {
        var p = Elite87();
        Assert.Same(p, Decode(Encode(p), p));
    }

    [Fact]
    public void Read_BuildsANewProfile_WhenAnyInputDiffers()
    {
        var p = Elite87();
        var other = new EnemyProfile(EnemyKind.Regular, 88, 2, Rarity.Elite, p.Modifiers, Defaults);
        var q = Decode(Encode(other), p);
        Assert.NotSame(p, q);
        Assert.Equal(88, q.Level);
    }

    [Fact]
    public void Encoding_IsCompact()
    {
        // level 1 B + header 1 B + count 1 B + 2 × (type 1 B + magnitude 1 B) = 7 B.
        Assert.Equal(7, Encode(Elite87()).Length);
    }

    [Fact]
    public void Read_ConsumesExactlyWhatWriteWrote()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            EnemyProfileCodec.Write(w, Elite87());
            w.Write((byte)0xAB);
        }
        ms.Position = 0;
        using var r = new BinaryReader(ms);
        EnemyProfileCodec.Read(r, null, Defaults);
        Assert.Equal(0xAB, r.ReadByte());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run `dotnet test --filter "FullyQualifiedName~EnemyProfileCodecTests"` in `BalanceLab/`.
Expected: a build error, `'EnemyProfileCodec' could not be found`.

- [ ] **Step 3: Add `SameInputs` to `EnemyProfile`**

In `EnemyProfile.cs`, add after the constructor:

```csharp
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
```

- [ ] **Step 4: Create the codec**

`ARPGEnemySystem/Common/Scaling/EnemyProfileCodec.cs`:

```csharp
using System;
using System.IO;
using ARPGEnemySystem.Common.GlobalNPCs;

namespace ARPGEnemySystem.Common.Scaling
{
    // Wire format for a profile's inputs; the receiver rebuilds everything derived.
    // level: 7-bit int | header byte: bit 0 kind, bits 1-2 phase, bits 3-5 rarity |
    // modifier count byte | per modifier: type byte + 7-bit magnitude.
    public static class EnemyProfileCodec
    {
        public static void Write(BinaryWriter w, EnemyProfile p)
        {
            w.Write7BitEncodedInt(p.Level);
            w.Write((byte)((int)p.Kind | (p.Phase << 1) | ((int)p.Rarity << 3)));
            w.Write((byte)p.Modifiers.Length);
            foreach (var m in p.Modifiers)
            {
                w.Write((byte)m.modifierType);
                w.Write7BitEncodedInt(m.magnitude);
            }
        }

        // Returns `current` itself when the inputs on the wire match it, so the movement packets that
        // repeat an unchanged profile allocate nothing.
        public static EnemyProfile Read(BinaryReader r, EnemyProfile current, ScalingSettings settings)
        {
            int level = r.Read7BitEncodedInt();
            byte header = r.ReadByte();
            var kind = (EnemyKind)(header & 1);
            int phase = (header >> 1) & 3;
            var rarity = (Rarity)((header >> 3) & 7);

            int count = r.ReadByte();
            Span<EnemyModifier> modifiers = stackalloc EnemyModifier[count];
            for (int i = 0; i < count; i++)
                modifiers[i] = new EnemyModifier((ModifierType)r.ReadByte(), r.Read7BitEncodedInt());

            if (current != null && current.SameInputs(kind, level, phase, rarity, modifiers))
                return current;
            return new EnemyProfile(kind, level, phase, rarity, modifiers.ToArray(), settings);
        }
    }
}
```

- [ ] **Step 5: Link the codec in BalanceLab**

In `BalanceLab/src/BalanceLab.csproj`, after the `EnemyProfile.cs` link line, add:

```xml
    <Compile Include="..\..\ARPGEnemySystem\Common\Scaling\EnemyProfileCodec.cs" Link="Linked\ES\EnemyProfileCodec.cs" />
```

- [ ] **Step 6: Run the tests to verify they pass**

Run `dotnet test --filter "FullyQualifiedName~EnemyProfileCodecTests"`, then `dotnet test`. Expected: all pass.

- [ ] **Step 7: Build the mod**

Run `dotnet build` in `ARPGEnemySystem/`. Expected: 0 `error CS`.

- [ ] **Step 8: Commit**

```bash
git -C ARPGEnemySystem add Common/Scaling/EnemyProfileCodec.cs Common/Scaling/EnemyProfile.cs
git -C ARPGEnemySystem commit -m "feat(scaling): compact wire format for enemy profiles"
git -C BalanceLab add src/BalanceLab.csproj tests/EnemyProfileCodecTests.cs
git -C BalanceLab commit -m "Link EnemyProfileCodec; pin round-trip and compactness"
```

---

### Task 3: ARPGEnemySystem runtime: profile owner, accessor, projectiles, tooltip

After this task ES builds. CS will **not** build until Task 4, which must follow immediately. In BalanceLab, `MirrorDriftTests` is expected to fail from here until Task 6, because it reports the deleted and changed mirrored files. Every other test must pass.

**Files:**
- Create: `ARPGEnemySystem/Common/GlobalNPCs/EnemyProfileNPC.cs`, `ARPGEnemySystem/Common/GlobalNPCs/EnemyStats.cs`
- Delete: `ARPGEnemySystem/Common/GlobalNPCs/NPCManager.cs`, `ARPGEnemySystem/Common/GlobalNPCs/BossManager.cs`
- Modify: `ARPGEnemySystem/Common/GlobalProjectiles/ProjectileManager.cs` (whole class body), `ARPGEnemySystem/Common/UI/NPCTooltip.cs:61-143`, `ARPGEnemySystem/ARPGEnemySystem.cs:25-39`, `ARPGEnemySystem/Common/Scaling/EnemyScaling.cs` (delete `BossElementalTier`)

**Interfaces:**
- Consumes: Tasks 1-2.
- Produces (ES namespace `ARPGEnemySystem.Common.GlobalNPCs`):
  - `EnemyProfileNPC : GlobalNPC` with `public EnemyProfile Profile` and `internal static ScalingSettings Settings()`.
  - `static class EnemyStats`:
    - `Profile(NPC) → EnemyProfile`, `ContactDamage(NPC) → int`, `Defense(NPC) → int`;
    - `Profile(Projectile) → EnemyProfile`, `ProjectileDamage(Projectile) → int`;
    - `DebuffDefenseReduction(NPC) → int`, with consts `IchorDefenseReduction = 15` and `BetsysCurseDefenseReduction = 40`.
  - `ProjectileManager` (namespace `ARPGEnemySystem.Common.GlobalProjectiles`) with `public EnemyProfile Profile` and `public int ShooterIndex = -1`.

- [ ] **Step 1: Create the accessor**

`ARPGEnemySystem/Common/GlobalNPCs/EnemyStats.cs`:

```csharp
using ARPGEnemySystem.Common.GlobalProjectiles;
using ARPGEnemySystem.Common.Scaling;
using Terraria;

namespace ARPGEnemySystem.Common.GlobalNPCs
{
    // The only way mod code reads an enemy's combat numbers. Vanilla AI rewrites npc.damage and
    // npc.defense mid-fight (phases, enrage — Plantera every frame), so the scaled value is computed
    // from whatever vanilla holds at the moment of the read.
    public static class EnemyStats
    {
        // Ichor and Betsy's Curse (NPC.cs:92084-92093). Vanilla subtracts these inside its own defense
        // step, which EnemyProfileNPC replaces, so they reach the mod's resistance through here.
        public const int IchorDefenseReduction = 15;
        public const int BetsysCurseDefenseReduction = 40;

        public static EnemyProfile Profile(NPC npc)
            => npc.TryGetGlobalNPC(out EnemyProfileNPC data) ? data.Profile : null;

        public static int ContactDamage(NPC npc)
        {
            var p = Profile(npc);
            return p == null ? npc.damage : p.ScaleDamage(npc.damage);
        }

        public static int Defense(NPC npc)
        {
            var p = Profile(npc);
            return p == null ? npc.defense : p.ScaleDefense(npc.defense);
        }

        public static int DebuffDefenseReduction(NPC npc)
            => (npc.ichor ? IchorDefenseReduction : 0) + (npc.betsysCurse ? BetsysCurseDefenseReduction : 0);

        public static EnemyProfile Profile(Projectile proj)
            => proj.TryGetGlobalProjectile(out ProjectileManager pm) ? pm.Profile : null;

        // The projectile's own damage field scaled by its shooter's profile. Vanilla's hostile ×2 and
        // difficulty multiplier are applied on top by the hurt pipeline, as vanilla does.
        public static int ProjectileDamage(Projectile proj)
        {
            var p = Profile(proj);
            return p == null ? proj.damage : p.ScaleDamage(proj.damage);
        }
    }
}
```

- [ ] **Step 2: Create the profile owner**

`ARPGEnemySystem/Common/GlobalNPCs/EnemyProfileNPC.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using ARPGEnemySystem.Common.Configs;
using ARPGEnemySystem.Common.GlobalProjectiles;
using ARPGEnemySystem.Common.Scaling;
using ARPGEnemySystem.Common.Systems;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace ARPGEnemySystem.Common.GlobalNPCs
{
    // Owns every combat NPC's EnemyProfile. Damage and defense are never written (EnemyStats scales
    // them at read time). Only max life, coin value and size are written: at spawn, and again after
    // every SetDefaults re-run on a live NPC, which re-creates all GlobalNPC instances
    // (GlobalLoaderUtils.cs:43-53).
    public class EnemyProfileNPC : GlobalNPC
    {
        public override bool InstancePerEntity => true;

        public EnemyProfile Profile;

        // Vanilla values the writes are computed from, recorded right after each SetDefaults.
        private int _baseLifeMax;
        private float _baseValue;

        // SetDefaults with a negative netID re-enters SetDefaults; only the outermost call carries the profile.
        private static int _setDefaultsDepth;

        public override bool AppliesToEntity(NPC entity, bool lateInstantiation)
            => entity.boss || (!entity.townNPC && !entity.friendly && !entity.CountsAsACritter && entity.type != NPCID.TargetDummy);

        public override void Load() => On_NPC.SetDefaults += SetDefaultsHook;

        internal static ScalingSettings Settings()
        {
            var cfg = ModContent.GetInstance<Config>();
            return new ScalingSettings(cfg.ScalingExponent, cfg.DefScalingExponent, cfg.DefenseFloor);
        }

        public override void OnSpawn(NPC npc, IEntitySource source)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;

            Profile = FightProfileOf(source)
                ?? (npc.boss || NPCID.Sets.ShouldBeCountedAsBoss[npc.type] ? NewFightProfile() : NewRegularProfile());

            RecordBase(npc);
            ApplyWrites(npc);
            npc.life = npc.lifeMax;
            BossPlayerScaling.Announce(npc, _baseLifeMax, Profile.Level);
        }

        // Spawn-record membership: anything a fight member spawns (directly, or through a projectile it
        // fired) joins that fight. The boss flag plays no part.
        private static EnemyProfile FightProfileOf(IEntitySource source)
        {
            if (source is not EntitySource_Parent parent)
                return null;

            EnemyProfile parentProfile = null;
            if (parent.Entity is NPC parentNpc)
                parentProfile = EnemyStats.Profile(parentNpc);
            else if (parent.Entity is Projectile parentProj)
                parentProfile = EnemyStats.Profile(parentProj);

            return parentProfile != null && parentProfile.Kind == EnemyKind.FightMember ? parentProfile : null;
        }

        private static EnemyProfile NewFightProfile()
        {
            int cap = WorldManager.levelCap;
            int level = Math.Clamp(Main.rand.Next(cap, (int)(cap * 1.25f)), 1, (int)(cap * 1.25f) + 1);
            return new EnemyProfile(EnemyKind.FightMember, level, ScalingMath.GetScalingPhase(),
                Rarity.None, Array.Empty<EnemyModifier>(), Settings());
        }

        private static EnemyProfile NewRegularProfile()
        {
            int cap = WorldManager.levelCap;
            int level = Math.Clamp(Main.rand.Next((int)(cap * 0.75f), (int)(cap * 1.1f)), 1, (int)(cap * 1.1f) + 1);
            var rarity = new EnemyRarity();
            var modifiers = new List<EnemyModifier>();
            int count = Utils.GetAmountOfEnemyModifier(rarity);
            for (int i = 0; i < count; i++)
                modifiers.Add(new EnemyModifier(Utils.CreateExcludeList(modifiers), Utils.GetTier()));
            return new EnemyProfile(EnemyKind.Regular, level, ScalingMath.GetScalingPhase(),
                rarity.rarity, modifiers.ToArray(), Settings());
        }

        private void RecordBase(NPC npc)
        {
            _baseLifeMax = npc.lifeMax;
            _baseValue = npc.value;
        }

        private void ApplyWrites(NPC npc)
        {
            npc.lifeMax = Math.Max(1, Profile.ScaleLifeMax(_baseLifeMax));
            npc.value = Profile.Kind == EnemyKind.Regular
                ? _baseValue * Utils.GetCoinMultiplier(new EnemyRarity(Profile.Rarity), Profile.Level, Profile.Modifiers.Length)
                : _baseValue;
            if (Profile.ScaleOverride is float scale)
                npc.scale = scale;
        }

        // Every path that re-runs SetDefaults on a live NPC: Transform and the EoW segment conversion
        // (SetDefaultsKeepPlayerInteraction), the natural spawner's variant SetDefaults right after
        // NewNPC (NPC.cs:88766-89251), and the client's NPC packet (MessageBuffer.cs:1823).
        // NewNPC always builds a fresh NPC (NPC.cs:91614), so a new spawn is inactive here and carries nothing.
        private static void SetDefaultsHook(On_NPC.orig_SetDefaults orig, NPC self, int type, NPCSpawnParams spawnparams)
        {
            _setDefaultsDepth++;
            bool outermost = _setDefaultsDepth == 1;
            bool wasActive = self.active;
            EnemyProfile carried = null;
            float lifeFraction = 1f;
            if (outermost && wasActive && self.TryGetGlobalNPC(out EnemyProfileNPC before) && before.Profile != null)
            {
                carried = before.Profile;
                lifeFraction = self.lifeMax > 0 ? (float)self.life / self.lifeMax : 1f;
            }

            try
            {
                orig(self, type, spawnparams);
            }
            finally
            {
                _setDefaultsDepth--;
            }

            if (!outermost || !wasActive || !self.TryGetGlobalNPC(out EnemyProfileNPC after))
                return;

            if (carried == null)
            {
                // A live NPC that just became a managed type (a critter turning into an enemy) —
                // its first profile, rolled on the server like a spawn. Clients get it from the packet.
                if (Main.netMode == NetmodeID.MultiplayerClient)
                    return;
                carried = NewRegularProfile();
                lifeFraction = 1f;
            }

            after.Profile = carried;
            after.RecordBase(self);
            after.ApplyWrites(self);
            self.life = Math.Max(1, (int)Math.Round(self.lifeMax * lifeFraction));
        }

        public override void SendExtraAI(NPC npc, BitWriter bitWriter, BinaryWriter binaryWriter)
        {
            bitWriter.WriteBit(Profile != null);
            // Vanilla's own full-life flag sets life to the client's pre-scaling lifeMax before this
            // data is read (MessageBuffer.cs:1843-1845); this bit lets the client restore it after scaling.
            bitWriter.WriteBit(npc.life == npc.lifeMax);
            if (Profile != null)
                EnemyProfileCodec.Write(binaryWriter, Profile);
        }

        public override void ReceiveExtraAI(NPC npc, BitReader bitReader, BinaryReader binaryReader)
        {
            bool hasProfile = bitReader.ReadBit();
            bool lifeFull = bitReader.ReadBit();
            if (!hasProfile)
                return;

            var received = EnemyProfileCodec.Read(binaryReader, Profile, Settings());
            if (ReferenceEquals(received, Profile))
                return;

            if (Profile == null)
                RecordBase(npc);
            Profile = received;
            ApplyWrites(npc);
            if (lifeFull)
                npc.life = npc.lifeMax;
        }

        // Vanilla's defense step is fully replaced (spec R5): defense acts only through the mod's
        // physical resistance. The flat bonus vanilla grants for negative defense
        // (NPC.cs:92076-92083) is cancelled too; negative defense converts through the mirrored curve instead.
        public override void ModifyIncomingHit(NPC npc, ref NPC.HitModifiers modifiers)
        {
            modifiers.Defense *= 0f;
            if (npc.defense < 0)
                modifiers.FlatBonusDamage += npc.defense;
        }

        public override void OnHitPlayer(NPC npc, Player target, Player.HurtInfo hurtInfo)
        {
            if (Profile == null)
                return;
            foreach (var m in Profile.Modifiers)
            {
                if (m.modifierType == ModifierType.SoulDrinker)
                    target.statMana -= m.magnitude;
            }
        }
    }
}
```

If the compiler rejects `On_NPC.SetDefaults` / `On_NPC.orig_SetDefaults`, check the generated names in `E:\Steam\steamapps\common\tModLoader\Libraries\TerrariaHooks\0.0.0.0\TerrariaHooks.dll`. `grep -a -o "orig_SetDefaults[A-Za-z_]*"` lists them. Use the NPC overload `(NPC self, int Type, NPCSpawnParams spawnparams)`, and cite the name you used in the report.

- [ ] **Step 3: Rewrite `ProjectileManager`**

Replace everything inside `namespace ARPGEnemySystem.Common.GlobalProjectiles { ... }` in `ARPGEnemySystem/Common/GlobalProjectiles/ProjectileManager.cs` with the block below. Trim the `using` list to what it needs: `System.IO`, `ARPGEnemySystem.Common.GlobalNPCs`, `ARPGEnemySystem.Common.Scaling`, `Terraria`, `Terraria.DataStructures`, `Terraria.ModLoader`, `Terraria.ModLoader.IO`.

```csharp
    public class ProjectileManager : GlobalProjectile
    {
        public override bool InstancePerEntity => true;

        // The shooter's profile (or the parent projectile's), shared by reference; null for projectiles
        // no enemy fired. It outlives the shooter, so a projectile keeps its scaling after its shooter dies.
        public EnemyProfile Profile;

        // The shooter's NPC slot — used only by the kaeshi counter-strike to find an attacker, never for scaling.
        public int ShooterIndex = -1;

        public override void OnSpawn(Projectile projectile, IEntitySource source)
        {
            if (source is not EntitySource_Parent parent)
                return;

            if (parent.Entity is NPC npc)
            {
                Profile = EnemyStats.Profile(npc);
                ShooterIndex = npc.whoAmI;
            }
            else if (parent.Entity is Projectile parentProj && parentProj.TryGetGlobalProjectile(out ProjectileManager parentPm))
            {
                Profile = parentPm.Profile;
                ShooterIndex = parentPm.ShooterIndex;
            }
        }

        public override void OnHitPlayer(Projectile projectile, Player target, Player.HurtInfo info)
        {
            if (Profile == null)
                return;
            foreach (var m in Profile.Modifiers)
            {
                if (m.modifierType == ModifierType.SoulDrinker)
                    target.statMana -= m.magnitude;
            }
        }

        public override void SendExtraAI(Projectile projectile, BitWriter bitWriter, BinaryWriter binaryWriter)
        {
            bitWriter.WriteBit(Profile != null);
            if (Profile == null)
                return;
            EnemyProfileCodec.Write(binaryWriter, Profile);
            binaryWriter.Write7BitEncodedInt(ShooterIndex + 1);
        }

        public override void ReceiveExtraAI(Projectile projectile, BitReader bitReader, BinaryReader binaryReader)
        {
            if (!bitReader.ReadBit())
                return;
            Profile = EnemyProfileCodec.Read(binaryReader, Profile, EnemyProfileNPC.Settings());
            ShooterIndex = binaryReader.Read7BitEncodedInt() - 1;
        }
    }
```

The Strong modifier no longer writes `projectile.damage`: it lives in the profile's transform, and `EnemyStats.ProjectileDamage` applies it.

- [ ] **Step 4: Rewrite the tooltip builder**

In `ARPGEnemySystem/Common/UI/NPCTooltip.cs`:

1. In `Update`, replace everything from `var cfg = ModContent.GetInstance<Config>();` through the end of the `else if (npc.TryGetGlobalNPC<BossManager>...` block (current lines 61-74) with:

```csharp
                var profile = EnemyStats.Profile(npc);
                if (profile == null) continue;

                string tooltipText = BuildTooltip(npc, profile);
```

   Keep the `if (tooltipText == null) continue;` line and everything after it.

2. Replace the three methods `BuildNormalTooltip`, `BuildBossTooltip` and `AppendCommonStats` (current lines 88-143) with:

```csharp
        private static string BuildTooltip(NPC npc, EnemyProfile p)
        {
            var sb = new StringBuilder();
            sb.Append(npc.GivenOrTypeName);
            sb.Append('\n');
            if (p.Kind == EnemyKind.FightMember)
                sb.Append(Language.GetTextValue(LocPrefix + "HeaderBoss", p.Level));
            else if (p.Modifiers.Length > 0)
                sb.Append(Language.GetTextValue(LocPrefix + "HeaderNormal", p.Level, p.Rarity,
                    string.Join(", ", p.Modifiers.Select(m => m.modifierType))));
            else
                sb.Append(Language.GetTextValue(LocPrefix + "HeaderNormalNoMods", p.Level, p.Rarity));

            // What a hit sees: scaled defense less Ichor / Betsy's Curse, through the same mirrored conversion.
            int defense = EnemyStats.Defense(npc) - EnemyStats.DebuffDefenseReduction(npc);
            float physRes = ElementalMath.ConvertDefenseToResistance(
                defense, ModContent.GetInstance<Config>().PhysResHalfPoint, ElementalMath.ElementCap);

            var pk = p.Package;
            sb.Append('\n');
            sb.Append(Language.GetTextValue(LocPrefix + "StatsLine", EnemyStats.ContactDamage(npc), defense, physRes.ToString("F1")));
            sb.Append('\n');
            sb.Append(Language.GetTextValue(LocPrefix + "Resistances",
                pk.FireResistance.ToString("F0"), pk.ColdResistance.ToString("F0"),
                pk.LightningResistance.ToString("F0"), pk.ChaosResistance.ToString("F0")));

            AppendElemDmg(sb, pk.FireDamagePct, pk.ColdDamagePct, pk.LightningDamagePct, pk.ChaosDamagePct);
            AppendPen(sb, pk.FirePen, pk.ColdPen, pk.LightningPen, pk.SunderingPct, pk.ChaosPen);
            return sb.ToString();
        }
```

3. Add `using ARPGEnemySystem.Common.Scaling;` to the usings.

- [ ] **Step 5: `GetEnemyInfo` from the profile**

In `ARPGEnemySystem/ARPGEnemySystem.cs`, replace the inner `if (args.Length >= 2 && args[1] is NPC npc) { ... }` block with the following, and add `using ARPGEnemySystem.Common.Scaling;`:

```csharp
				if (args.Length >= 2 && args[1] is NPC npc && npc.TryGetGlobalNPC(out EnemyProfileNPC data))
				{
					// Level 0 = managed but not yet synced to this client; callers keep polling.
					if (data.Profile == null)
						return new int[] { 0, 0 };
					int rarityTier = data.Profile.Kind == EnemyKind.FightMember ? 0 : (int)data.Profile.Rarity;
					return new int[] { data.Profile.Level, rarityTier };
				}
```

- [ ] **Step 6: Delete the old managers and the dead helper**

```bash
git -C ARPGEnemySystem rm Common/GlobalNPCs/NPCManager.cs Common/GlobalNPCs/BossManager.cs
```

In `EnemyScaling.cs`, delete `public static int BossElementalTier() => ...` and its now-unused `using Terraria;` if nothing else in the file needs it. `ApplyBossElementals` doesn't use it.

- [ ] **Step 7: Verify nothing in ES still names the old types**

Run `grep -rn "NPCManager\|BossManager\|BossElementalTier\|modNPC\|modBossNPC\|npcIndex" ARPGEnemySystem --include=*.cs`.
Expected: no output. Fix any hit before building.

- [ ] **Step 8: Build ES**

Run `dotnet build` in `ARPGEnemySystem/`. Expected: 0 `error CS`. CS is not built in this task.

- [ ] **Step 9: Write the in-game checks for the Review Focus items**

Append this to the report, for the user's in-game pass after Task 4. Don't run it now.

1. **Natural-spawn variants.**
   - Spawn a green slime and a zombie variant naturally at night.
   - Hover over each: the tooltip level matches `/checklevelcap`'s range, and max life equals the vanilla variant's life × the level multiplier, **once**.
   - Also summon a blue slime by netID with DragonLens (negative netID) and check its HP is scaled once.
2. **Multiplayer first sight.** Host and join. The joining client sees full-HP enemies at their scaled max, with a full health bar.
3. **Multiplayer slot reuse.** Kill many weak enemies near a spawner for a minute on a client. No enemy ever shows more than its scaled max life.
4. **Projectile chains.** Fight Duke Fishron: Sharkrons show the Duke's level with no rarity. Dart traps hit for vanilla damage.
5. **Wall spiders.** A Wall Creeper hit to half HP, then crawling onto a wall, keeps half HP and its level.

- [ ] **Step 10: Commit**

```bash
git -C ARPGEnemySystem add Common/GlobalNPCs/EnemyProfileNPC.cs Common/GlobalNPCs/EnemyStats.cs Common/GlobalProjectiles/ProjectileManager.cs Common/UI/NPCTooltip.cs ARPGEnemySystem.cs Common/Scaling/EnemyScaling.cs
git -C ARPGEnemySystem commit -m "refactor(scaling): scale at read time from one immutable profile; carry it across SetDefaults; compact sync"
```

---

### Task 4: ARPGCharacterSystem consumers and combat maths

**Files:**
- Create: `ARPGCharacterSystem/Common/Combat/VanillaDefenseReducers.cs`
- Modify: `ARPGCharacterSystem/Common/Combat/TargetResistances.cs:58-104`
- Modify: `ARPGCharacterSystem/Common/Combat/ElementalDamageCalculator.cs:29-69`
- Modify: `ARPGCharacterSystem/Common/Players/OutgoingHitPlayer.cs:106-121` and `:238-240`
- Modify: `ARPGCharacterSystem/Common/Combat/ReapResolver.cs:68`
- Modify: `ARPGCharacterSystem/Common/Players/PlayerHurtPipeline.cs` (usings, branches A and B `:72-160`, `RegisterHandler` `:162-205`, kaeshi `:416-421`)
- Modify: `ARPGCharacterSystem/Common/GlobalNPCs/XPAwardGlobalNPC.cs:68-97`

**Interfaces:**
- Consumes: `EnemyStats`, `EnemyProfile`, `EnemyKind`, `EnemyStatBlock`, and `ProjectileManager.Profile`/`ShooterIndex` (Task 3).
- Produces:
  - `readonly struct VanillaDefenseReducers(float Flat, float ScalingFraction)` with `static DebuffsOnly(NPC)` and `static ForHit(Player, in HitContext)`.
  - `TargetResistances.Read(NPC target, List<Affix> affixes, PlayerElementalStats playerElem, float halfPoint, float cap, in VanillaDefenseReducers vanilla)`.
  - `ElementalDamageCalculator.ApplyToHit(..., in DamagePool pool, in VanillaDefenseReducers vanillaDefense, ref NPC.HitModifiers modifiers, ...)`.

- [ ] **Step 1: Create the reducers**

`ARPGCharacterSystem/Common/Combat/VanillaDefenseReducers.cs`:

```csharp
using ARPGCharacterSystem.Common.Players;
using ARPGEnemySystem.Common.GlobalNPCs;
using Terraria;
using Terraria.ID;

namespace ARPGCharacterSystem.Common.Combat
{
    // Everything vanilla uses to lower an enemy's defense, ported whole so it reaches the mod's
    // physical resistance (ARPGEnemySystem zeroes vanilla's own defense step). Read from the sources
    // vanilla reads, not from the hit's modifiers: vanilla adds some of them after the mod's hooks run
    // (Player.cs:19361-19363 direct strikes; Projectile.cs:12294 vs 12914).
    public readonly struct VanillaDefenseReducers
    {
        public readonly float Flat;             // defense points removed
        public readonly float ScalingFraction;  // share of positive defense ignored, applied before Flat

        public VanillaDefenseReducers(float flat, float scalingFraction)
        {
            Flat = flat;
            ScalingFraction = scalingFraction;
        }

        public static VanillaDefenseReducers DebuffsOnly(NPC target)
            => new(EnemyStats.DebuffDefenseReduction(target), 0f);

        public static VanillaDefenseReducers ForHit(Player player, in HitContext ctx)
        {
            float flat = EnemyStats.DebuffDefenseReduction(ctx.Target);
            float scaling = 0f;
            if (ctx.Projectile != null)
            {
                flat += ctx.Projectile.ArmorPenetration;                                   // Projectile.cs:12293
                if (IsLightningAura(ctx.Projectile.type) && ctx.Target.type != NPCID.DungeonGuardian
                    && ctx.Target.defense < 999)
                    scaling = 1f;                                                          // Projectile.cs:12908-12914
            }
            else if (ctx.Item != null)
            {
                flat += player.GetWeaponArmorPenetration(ctx.Item);                        // Player.cs:44170
                if (ctx.Item.type == ItemID.Flymeal && ctx.Target.isLikeATownNPC)
                    scaling = 1f;                                                          // Player.cs:44082-44171
            }
            else
            {
                // Direct strike (ApplyDamageToNPC): the class total (Player.cs:19363) plus the repeated
                // weapon's own pen, as the item path would have added.
                flat += player.GetTotalArmorPenetration(ctx.DamageClass) + ctx.WeaponArmorPenetration;
            }
            return new VanillaDefenseReducers(flat, scaling);
        }

        private static bool IsLightningAura(int type)
            => type == ProjectileID.DD2LightningAuraT1 || type == ProjectileID.DD2LightningAuraT2
            || type == ProjectileID.DD2LightningAuraT3;
    }
}
```

`ctx.Target.defense < 999` is a raw read that reproduces vanilla's own guard, and it's allowed by name in Task 5's guard test.

- [ ] **Step 2: Rewrite `TargetResistances.Read`**

Replace the `Read` method (lines 58-104) of `ARPGCharacterSystem/Common/Combat/TargetResistances.cs` with the following, and add `using ARPGEnemySystem.Common.Scaling;`:

```csharp
        public static TargetResistances Read(NPC target, List<Affix> affixes,
                                             PlayerElementalStats playerElem,
                                             float halfPoint, float cap,
                                             in VanillaDefenseReducers vanilla)
        {
            EnemyProfile profile = EnemyStats.Profile(target);
            float fireRes  = profile?.Package.FireResistance      ?? 0f;
            float coldRes  = profile?.Package.ColdResistance      ?? 0f;
            float lightRes = profile?.Package.LightningResistance ?? 0f;
            float chaosRes = profile?.Package.ChaosResistance     ?? 0f;

            // Pen reduces the stored % directly with no floor (spec R8); ApplyResistance clamps only at the cap.
            float universalPen = GetMagnitude(affixes, AffixId.AllElementalPenetration);
            float firePen  = GetMagnitude(affixes, AffixId.FirePenetration)      + universalPen + playerElem.FirePen;
            float coldPen  = GetMagnitude(affixes, AffixId.ColdPenetration)      + universalPen + playerElem.ColdPen;
            float lightPen = GetMagnitude(affixes, AffixId.LightningPenetration) + universalPen + playerElem.LightningPen;
            // Chaos deliberately does NOT receive AllElementalPenetration (F/C/L only, per spec).
            float chaosPen = GetMagnitude(affixes, AffixId.ChaosPenetration)     + playerElem.ChaosPen;

            float flatArmorPen = GetMagnitude(affixes, AffixId.FlatArmorPen) + playerElem.FlatArmorPen;
            float percArmorPen = GetMagnitude(affixes, AffixId.PercentageArmorPen) + playerElem.PercentArmorPen;

            // Armour pen (the mod's and vanilla's) lowers the defense physRes is converted from, in
            // vanilla's order: percentage first, then flat (NPC.cs:317-319). No floor: defense below zero
            // converts to a vulnerability. Percentage pen only shrinks positive defense, so it never
            // lessens a vulnerability.
            float scaledDefense = EnemyStats.Defense(target);
            float effectiveDefense = scaledDefense;
            if (effectiveDefense > 0f)
                effectiveDefense *= 1f - (percArmorPen / 100f + vanilla.ScalingFraction);
            effectiveDefense -= flatArmorPen + vanilla.Flat;

            return new TargetResistances(
                ElementalMath.ConvertDefenseToResistance(effectiveDefense, halfPoint, cap),
                fireRes - firePen, coldRes - coldPen, lightRes - lightPen, chaosRes - chaosPen,
                ElementalMath.ConvertDefenseToResistance(scaledDefense, halfPoint, cap),
                fireRes, coldRes, lightRes, chaosRes,
                flatArmorPen + vanilla.Flat, percArmorPen + vanilla.ScalingFraction * 100f,
                universalPen, firePen, coldPen, lightPen, chaosPen);
        }
```

Delete `using ARPGEnemySystem.Common.GlobalNPCs;` only if it's now unused. It isn't, because `EnemyStats` lives there.

- [ ] **Step 3: Pass the reducers through the calculator and the reap**

In `ElementalDamageCalculator.cs`:
- Add the parameter `in VanillaDefenseReducers vanillaDefense` to `ApplyToHit`, directly after `in DamagePool pool,`.
- Change line 69 to `var res = TargetResistances.Read(target, affixes, playerElem, halfPoint, cap, in vanillaDefense);`.
- Replace the hook-ordering comment block (lines 29-47) with:

```csharp
        // Registers a ModifyHitInfo callback that computes elemental damage and applies all resistances.
        //
        // Vanilla's defense step is zeroed for every managed enemy (EnemyProfileNPC.ModifyIncomingHit),
        // so info.Damage in the callback is weapon output × crit with no defense subtraction. Defense
        // acts only through physRes, read here from EnemyStats.Defense (scaled at read time) less the
        // mod's and vanilla's armour pen and Ichor/Betsy's Curse (VanillaDefenseReducers). That read
        // never depends on whether ModifyIncomingHit ran before or after this hook.
        //
        // Elemental base = info.Damage (crit, ammo, class bonuses all already included).
        // Crit propagates to elemental proportionally — no undo needed.
```

In `ReapResolver.cs` line 68, change to:

```csharp
            var res = TargetResistances.Read(target, weaponAffixes, elem, cfg.PhysResHalfPoint, cap, VanillaDefenseReducers.DebuffsOnly(target));
```

- [ ] **Step 4: Build the reducers from the hit context**

In `OutgoingHitPlayer.cs`:
- In `ModifyHitNPC` (lines 106-121), delete the comment "Vanilla's item path adds..." (3 lines) and the line `modifiers.ArmorPenetration += ctx.WeaponArmorPenetration;`. With vanilla's defense step zeroed that line has no effect; `VanillaDefenseReducers.ForHit` now carries that pen.
- Change the `ApplyToHit` call (lines 238-240) to:

```csharp
            var vanillaDefense = VanillaDefenseReducers.ForHit(Player, in ctx);
            ElementalDamageCalculator.ApplyToHit(ctx.WeaponAffixes, Player, ctx.Target,
                                                 ctx.WeaponBaseDamage, in familyConvert,
                                                 in pool, in vanillaDefense, ref modifiers,
                                                 _chargeSpendThisHit.GainLightningPct, _chargeSpendThisHit.MoreDamage);
```

- [ ] **Step 5: Hurt pipeline: profile branches, vanilla projectile maths, no floors**

In `PlayerHurtPipeline.cs`:

1. **Usings:** add `using ARPGEnemySystem.Common.Scaling;` and `using Terraria.GameContent.Creative;`. Keep `using Terraria.ID;`.
2. **Branch A.** Replace everything from `if (!proj.TryGetGlobalProjectile<EnemyProjectileManager>(out var pm)) return;` through the branch's closing `return;` with:

```csharp
                // No enemy behind it (traps, boulders): vanilla math runs unchanged.
                if (!proj.TryGetGlobalProjectile<EnemyProjectileManager>(out var pm) || pm.Profile == null) return;

                string sourceName = pm.ShooterIndex >= 0 && pm.ShooterIndex < Main.maxNPCs
                    ? Main.npc[pm.ShooterIndex].GivenOrTypeName : "Unknown";
                RegisterHandler(ref modifiers, EnemyProjectileBase(proj), pm.Profile.Package, sourceName, isProj: true);
                return;
```

3. **Branch B.** Replace everything from `float firePct, coldPct, lightPct, chaosPct;` through the branch's closing `return;` with:

```csharp
                var profile = EnemyStats.Profile(npc);
                if (profile == null) return;

                // Contact hits use vanilla ±15% damage variance on the level-scaled contact damage.
                RegisterHandler(ref modifiers, Main.DamageVar(EnemyStats.ContactDamage(npc)), profile.Package,
                    npc.GivenOrTypeName, isProj: false);
                return;
```

4. **`EnemyProjectileBase`.** Add this method directly above `RegisterHandler`:

```csharp
        // Vanilla's enemy-projectile damage on the shooter-scaled damage field: DamageVar × 2
        // (Projectile.cs:13804), Warmth's 0.7× on cold projectiles and the difficulty multiplier,
        // skipped for reflected projectiles and PlayerHurtDamageIgnoresDifficultyScaling
        // (CombinedHooks.cs:229-246).
        private float EnemyProjectileBase(Projectile proj)
        {
            float damage = Main.DamageVar(EnemyStats.ProjectileDamage(proj)) * 2f;
            if (Player.resistCold && proj.coldDamage)
                damage *= 0.7f;
            if (proj.reflected || ProjectileID.Sets.PlayerHurtDamageIgnoresDifficultyScaling[proj.type])
                return damage;

            float difficulty = Main.GameModeInfo.EnemyDamageMultiplier;
            if (Main.GameModeInfo.IsJourneyMode)
            {
                var power = CreativePowerManager.Instance.GetPower<CreativePowers.DifficultySliderPower>();
                if (power.GetIsUnlocked())
                    difficulty = power.StrengthMultiplierToGiveNPCs;
            }
            return damage * difficulty;
        }
```

5. **`RegisterHandler`.** Change the signature to:

```csharp
        private void RegisterHandler(ref Player.HurtModifiers modifiers,
                                      float baseDamage,
                                      EnemyStatBlock package,
                                      string sourceName, bool isProj)
```

   Make the first lines of its body:

```csharp
            float firePct = package.FireDamagePct, coldPct = package.ColdDamagePct;
            float lightPct = package.LightningDamagePct, chaosPct = package.ChaosDamagePct;
            float firePen = package.FirePen, coldPen = package.ColdPen, lightPen = package.LightningPen;
            float sunderingPct = package.SunderingPct, chaosPen = package.ChaosPen;
```

6. **Remove the player-side floors (spec R8).** In `RegisterHandler`:
   - `int effDef = Math.Max(0, (int)(Player.statDefense * (1f - sunderingPct / 100f)));` becomes `int effDef = (int)(Player.statDefense * (1f - sunderingPct / 100f));`
   - each `float effXRes = Math.Max(0f, elem.XRes - xPen);` becomes `float effXRes = elem.XRes - xPen;`, for all four elements.
   - Update the two comments above them so they no longer say "floored at 0".
7. **Kaeshi.** In `TryCounterStrike`, replace `&& pm.npcIndex >= 0 && pm.npcIndex < Main.maxNPCs)` and `attacker = Main.npc[pm.npcIndex];` with `pm.ShooterIndex` in both places.

- [ ] **Step 6: XP from the accessor and the profile**

In `XPAwardGlobalNPC.cs`:
- In `ComputeBaseXp`, change the first line to `long baseXp = npc.lifeMax / 5 + EnemyStats.ContactDamage(npc) * 2 + EnemyStats.Defense(npc) * 2;`.
- Replace `ResolveNpcLevel` and `ResolveXpMultiplier` with the code below, and add `using ARPGEnemySystem.Common.Scaling;`:

```csharp
        private static int ResolveNpcLevel(NPC npc) => EnemyStats.Profile(npc)?.Level ?? 0;

        private static float ResolveXpMultiplier(NPC npc, int level)
        {
            var p = EnemyStats.Profile(npc);
            if (p == null || p.Kind != EnemyKind.Regular) return 1f;
            return EnemyUtils.GetXPMultiplier(new EnemyRarity(p.Rarity), level, p.Modifiers.Length);
        }
```

- [ ] **Step 7: Verify no CS code names the removed members**

Run `grep -rn "NPCManager\|BossManager\|modNPC\|modBossNPC\|npcIndex" ARPGCharacterSystem --include=*.cs`.
Expected: no output.

- [ ] **Step 8: Build both mods**

Run `dotnet build` in `ARPGEnemySystem/`, then in `ARPGCharacterSystem/`. Expected: 0 `error CS` in both.

- [ ] **Step 9: Run BalanceLab**

Run `dotnet test` in `BalanceLab/`. Expected: only `MirrorDriftTests.MirroredModFiles_MatchRecordedFingerprints` fails, listing the changed and deleted mirrored files. Anything else failing is a real problem. Report it.

- [ ] **Step 10: Commit**

```bash
git -C ARPGCharacterSystem add Common/Combat/VanillaDefenseReducers.cs Common/Combat/TargetResistances.cs Common/Combat/ElementalDamageCalculator.cs Common/Combat/ReapResolver.cs Common/Players/OutgoingHitPlayer.cs Common/Players/PlayerHurtPipeline.cs Common/GlobalNPCs/XPAwardGlobalNPC.cs
git -C ARPGCharacterSystem commit -m "refactor(combat): read enemy stats through EnemyStats; vanilla defense reducers reach physRes; enemy projectiles scale; no resistance floors"
```

---

### Task 5: Guard test against raw enemy stat reads

**Files:**
- Test: `BalanceLab/tests/RawEnemyStatReadTests.cs`

**Interfaces:**
- Consumes: the final file layout from Tasks 3-4.

- [ ] **Step 1: Write the test**

`BalanceLab/tests/RawEnemyStatReadTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

// Enemy damage/defense must be read through ARPGEnemySystem's EnemyStats: vanilla AI rewrites the raw
// fields mid-fight, so a raw read silently loses the level scaling. The allowed files below are the
// accessor itself and the places that deliberately handle vanilla's raw value.
public class RawEnemyStatReadTests
{
    static string ModSourcesRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static readonly string[] ModRoots = { "ARPGEnemySystem", "ARPGCharacterSystem", "ARPGItemSystem" };

    static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "ARPGEnemySystem/Common/GlobalNPCs/EnemyStats.cs",              // the accessor
        "ARPGEnemySystem/Common/GlobalNPCs/EnemyProfileNPC.cs",         // cancels vanilla's negative-defense bonus
        "ARPGCharacterSystem/Common/Combat/VanillaDefenseReducers.cs",  // vanilla's own `defense < 999` guard
    };

    static readonly Regex NpcStatRead = new(@"\b(npc|target|attacker|self|nPC\d*|Main\.npc\[[^\]]+\])\.(damage|defense)\b", RegexOptions.Compiled);
    static readonly Regex EnemyProjectileRawDamage = new(@"\bproj\.damage\b", RegexOptions.Compiled);

    static IEnumerable<(string Rel, int Line, string Text)> CodeLines()
    {
        foreach (var root in ModRoots)
        {
            string dir = Path.Combine(ModSourcesRoot, root);
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(ModSourcesRoot, file).Replace('\\', '/');
                if (rel.Contains("/bin/") || rel.Contains("/obj/")) continue;
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string code = lines[i];
                    int comment = code.IndexOf("//", StringComparison.Ordinal);
                    if (comment >= 0) code = code.Substring(0, comment);
                    yield return (rel, i + 1, code);
                }
            }
        }
    }

    [Fact]
    public void NoModCodeReadsRawEnemyDamageOrDefense()
    {
        var offenders = CodeLines()
            .Where(l => !Allowed.Contains(l.Rel) && NpcStatRead.IsMatch(l.Text))
            .Select(l => $"{l.Rel}:{l.Line}: {l.Text.Trim()}")
            .ToList();
        Assert.True(offenders.Count == 0,
            "Read enemy damage/defense through EnemyStats (ARPGEnemySystem/Common/GlobalNPCs/EnemyStats.cs):\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void HurtPipelineNeverUsesAnEnemyProjectilesRawDamage()
    {
        var offenders = CodeLines()
            .Where(l => l.Rel.EndsWith("ARPGCharacterSystem/Common/Players/PlayerHurtPipeline.cs", StringComparison.OrdinalIgnoreCase)
                        && EnemyProjectileRawDamage.IsMatch(l.Text))
            .Select(l => $"{l.Rel}:{l.Line}: {l.Text.Trim()}")
            .ToList();
        Assert.True(offenders.Count == 0,
            "Enemy projectile damage must come from EnemyStats.ProjectileDamage:\n" + string.Join("\n", offenders));
    }
}
```

- [ ] **Step 2: Prove the test bites**

1. Temporarily add `int probe = npc.defense;` inside any method of `ARPGCharacterSystem/Common/GlobalNPCs/XPAwardGlobalNPC.cs`.
2. Run `dotnet test --filter "FullyQualifiedName~RawEnemyStatReadTests"`. Expected: FAIL, listing that line.
3. Remove the probe line.

- [ ] **Step 3: Run the test on the real tree**

Run `dotnet test --filter "FullyQualifiedName~RawEnemyStatReadTests"`. Expected: PASS.

If it fails on a line that is genuinely vanilla-semantics, don't widen the regex. Move that read into `EnemyStats`, or add the file to `Allowed` with a one-line reason, and say which in the report.

- [ ] **Step 4: Commit**

```bash
git -C BalanceLab add tests/RawEnemyStatReadTests.cs
git -C BalanceLab commit -m "Guard test: enemy damage/defense only through EnemyStats"
```

---

### Task 6: BalanceLab re-sync and eval round 4

**Files:**
- Modify: `BalanceLab/src/Sim/EnemyState.cs` (fields, `FromNpc`, `FromBoss`)
- Modify: `BalanceLab/src/Model/BossData.cs`
- Modify: `BalanceLab/src/EngineConstants.cs`
- Modify: `BalanceLab/data/vanilla/bosses/{eye-of-cthulhu,plantera,moon-lord,the-destroyer}.json`
- Modify: `BalanceLab/src/Sim/HitResolver.cs:145-153,285-297,362,503-527`
- Modify: `BalanceLab/src/Sim/ReapSim.cs:82`
- Modify: `BalanceLab/src/Sim/SurvivalCalc.cs:84-102,140-155`
- Modify: `BalanceLab/src/Report/EvalRunner.cs:75-77`
- Modify: `BalanceLab/tests/EnemyScalingTests.cs` and any test that fails after the change
- Modify: `BalanceLab/tests/mirrored-sources.json`, `BalanceLab/docs/ASSUMPTIONS.md`, `BalanceLab/CLAUDE.md`
- Create: `BalanceLab/reports/2026-09-29-eval-round4-scaling-refactor.txt`

**Interfaces:**
- Consumes: `EnemyProfile`, `EnemyKind`, `ScalingSettings` (linked, Task 1).
- Produces: `EnemyState.FromBoss(BossData boss, int level, int phase, string difficulty)`, with the `levelCap` parameter removed.

- [ ] **Step 1: Write the new failing tests**

In `BalanceLab/tests/EnemyScalingTests.cs`, **delete** these six tests, which pin behaviour the refactor removes:
- `FromBoss_BiggestHitIsProjectile_OverrideUsesRawSeedNotScaledDamage`
- `FromBoss_BiggestHitIsProjectileFalse_OverrideStaysNull`
- `FromBoss_AiResetsDamageDefense_ScalesLifeMaxOnlyNotDamageOrDefense`
- `FromBoss_AiResetsDamageDefenseFalse_ScalesDamageAndDefenseAsBefore`
- `FromBoss_HitPartIsRegularEnemy_ScalesHitPartDefenseAtRegularEnemyMeanLevel`
- `FromBoss_HitPartIsRegularEnemyFalse_UsesOrdinaryBossDefense`

Then add:

```csharp
    // Every boss is scaled the same way now: the fight profile scales damage and defense at read
    // time (EnemyStats.cs), so an AI that rewrites them mid-fight no longer escapes the level.
    [Fact]
    public void FromBoss_ScalesLifeDamageAndDefense_ThroughTheStagedLevelScaling()
    {
        var boss = new BossData("b", "B", HpClassic: 1000, HpExpert: 1400, ContactDamageExpert: 40,
            ProjectileDamageFactor: 1f, DefenseClassic: 20, HitUptime: new(), Parts: System.Array.Empty<BossPart>(), Source: "test");
        var e = EnemyState.FromBoss(boss, level: 60, phase: 1, difficulty: "expert");

        var expected = new EnemyStatBlock { LifeMax = 1400, Damage = 40, Defense = 20 };
        EnemyScaling.ApplyLevelScaling(ref expected, 60, 1, ScalingDefaults.ScalingExponent, ScalingDefaults.DefScalingExponent, ScalingDefaults.DefenseFloor);
        Assert.Equal(expected.LifeMax, e.Stats.LifeMax);
        Assert.Equal(expected.Damage, e.Stats.Damage);
        Assert.Equal(expected.Defense, e.Stats.Defense);
        Assert.Equal(30f, e.Stats.FireDamagePct);   // phase 1 -> boss tier 1 package
    }

    // The part the player actually hits (Destroyer body) is a fight member: the BOSS's level, its own base defense.
    [Fact]
    public void FromBoss_HitPartDefense_IsScaledAtTheBossLevel()
    {
        var boss = new BossData("d", "D", HpClassic: 80000, HpExpert: 120000, ContactDamageExpert: 70,
            ProjectileDamageFactor: 1f, DefenseClassic: 0, HitUptime: new(), Parts: System.Array.Empty<BossPart>(),
            Source: "test", HitPartDefense: 30);
        var e = EnemyState.FromBoss(boss, level: 117, phase: 1, difficulty: "expert");

        var expected = new EnemyStatBlock { Defense = 30 };
        EnemyScaling.ApplyLevelScaling(ref expected, 117, 1, ScalingDefaults.ScalingExponent, ScalingDefaults.DefScalingExponent, ScalingDefaults.DefenseFloor);
        Assert.Equal(expected.Defense, e.Stats.Defense);
    }
```

In `BalanceLab/tests/HitResolverTests.cs`, add the Review Focus #4 pin. Use the file's existing fixture helpers to build a resolver against an `EnemyState` with `Stats.Defense = -40` and a weapon carrying `PercentageArmorPen` 50. Assert `ResistanceOf(Element.Physical)` equals `ElementalMath.ConvertDefenseToResistance(-40f, 60f, 75f)`, which is −30.0 (`-75 × 40 / 100`). Name it `NegativeDefense_PercentPenLeavesVulnerabilityUntouched`, and put that arithmetic in its comment.

- [ ] **Step 2: Run the tests to verify they fail**

Run `dotnet test --filter "FullyQualifiedName~EnemyScalingTests|FullyQualifiedName~HitResolverTests"`.
Expected: build errors (`FromBoss` has no `HitPartDefense` path, or the signature differs) or assertion failures.

- [ ] **Step 3: `BossData`, data files, constants**

- In `BossData.cs`:
  - remove the parameters `bool BiggestHitIsProjectile = false, bool AiResetsDamageDefense = false, bool HitPartIsRegularEnemy = false` (keep `int HitPartDefense = 0`);
  - replace the long comment block above the record with:

```csharp
    // HitUptime keys: Melee/Ranged/Magic/Summon. Vanilla Expert does not change boss defense,
    // so there is no separate expert-defense field.
    // HitPartDefense: when > 0, the base defense of the part the player actually hits (Destroyer
    // body/tail). It is a fight member (EnemyProfileNPC.cs): scaled at the boss's level, no rarity.
```

- Remove the keys `BiggestHitIsProjectile`, `AiResetsDamageDefense` and `HitPartIsRegularEnemy` from the four boss JSON files. Rewrite each file's `Source` text that explains them into a short statement of what the numbers are.
- In `EngineConstants.cs`, delete `HostileProjectileDamageEngineMultiplier` and its comment. Run `grep -rn HostileProjectileDamageEngineMultiplier src tests` and resolve every use; `VanillaCombat.cs` comments included.

- [ ] **Step 4: `EnemyState`**

- Delete the fields `HitPartIsRegularEnemy` and `BiggestHitBaseOverride` and their comments.
- Change the class comment's "(double-dip defense — see HitResolver)" to "(boss fights share one profile — EnemyProfileNPC.cs)".
- Replace `FromNpc` and `FromBoss` with:

```csharp
        static readonly ScalingSettings DefaultSettings = new(ScalingDefaults.ScalingExponent, ScalingDefaults.DefScalingExponent, ScalingDefaults.DefenseFloor);

        public static EnemyState FromNpc(NpcDump npc, int level, int phase)
        {
            // A regular enemy at a fixed level, without rarity or modifiers (ASSUMPTIONS.md).
            var profile = new EnemyProfile(EnemyKind.Regular, level, phase, Rarity.None, Array.Empty<EnemyModifier>(), DefaultSettings);
            var stats = profile.Package;
            stats.LifeMax = profile.ScaleLifeMax(npc.LifeMax);
            stats.Damage = profile.ScaleDamage(npc.Damage);
            stats.Defense = profile.ScaleDefense(npc.Defense);
            return new EnemyState(stats, stats.LifeMax, isBoss: false, hitUptime: FullUptime());
        }

        // Every NPC in a boss fight shares the boss's profile (EnemyProfileNPC.cs, spawn-record membership).
        // Damage and defense are scaled at read time (EnemyStats.cs), so AI rewrites keep the scaling.
        public static EnemyState FromBoss(BossData boss, int level, int phase, string difficulty)
        {
            var profile = new EnemyProfile(EnemyKind.FightMember, level, phase, Rarity.None, Array.Empty<EnemyModifier>(), DefaultSettings);
            var stats = profile.Package;
            stats.LifeMax = profile.ScaleLifeMax(BossHp(boss, difficulty));
            stats.Damage = profile.ScaleDamage(SeedContactDamage(boss, difficulty));
            stats.Defense = profile.ScaleDefense(boss.HitPartDefense > 0 ? boss.HitPartDefense : boss.DefenseClassic);

            var state = new EnemyState(stats, stats.LifeMax, isBoss: true, hitUptime: boss.HitUptime);
            state.BiggestHitFactor = Math.Max(1f, boss.ProjectileDamageFactor);
            return state;
        }
```

- Add `using ARPGEnemySystem.Common.GlobalNPCs;`.
- In `EvalRunner.cs` lines 75 and 77, drop the trailing `, cap` argument from both `FromBoss` calls.

- [ ] **Step 5: `HitResolver`, `ReapSim`, `SurvivalCalc`**

- **`HitResolver.cs`:**
  - delete `ApplyBossDefense` and its comment (lines 145-153);
  - make `ComputeAfterDefenseBase` end with `return preCritBase;` and replace its three-line boss comment with `// No defense subtraction: vanilla's defense step is zeroed for every enemy (EnemyProfileNPC.ModifyIncomingHit).`;
  - change line 362 to `float afterDefense = preCritBase;` (keep the variable if later lines read it);
  - in `ComputeResistances`, replace the three `effectiveDefense` lines with:

```csharp
            float effectiveDefense = _enemy.Stats.Defense;
            if (effectiveDefense > 0f) effectiveDefense *= 1f - percArmorPen / 100f;
            effectiveDefense -= flatArmorPen;
```

    and update its comment's citation to `TargetResistances.Read` (TargetResistances.cs), noting there is no floor. Vanilla reducers (Ichor, vanilla armor pen) are not modelled; that goes in ASSUMPTIONS in step 7.
  - delete every remaining `HitPartIsRegularEnemy` reference.
- **`ReapSim.cs:82`:** `total += Math.Max(1, (int)MathF.Round(rawAmount * mult));`. Remove the boss-defense wording from its comment.
- **`SurvivalCalc.cs`:**
  - `int effDef = (int)(statDefense * (1 - e.Stats.SunderingPct / 100.0));`
  - the four `effXRes` lines lose their `Math.Max(0f, …)`;
  - the biggest-hit line becomes `double mitigatedBiggest = MitigateHit(Math.Floor(baseDamage * e.BiggestHitFactor), e, mitigation);` and its long comment shrinks to `// Biggest regular hit: level-scaled contact × ProjectileDamageFactor; projectiles scale with their shooter (EnemyStats.ProjectileDamage).`

- [ ] **Step 6: Run the suite and re-pin honestly**

Run `dotnet test`. Tests that pinned the boss `defense/2` subtraction, the AI-reset case, the regular-enemy hit part or the player-side floors will fail. For each one:
- re-derive the expected number by hand from the new rules;
- write that arithmetic in the test comment, citing the mod line it follows (`EnemyProfileNPC.cs`, `TargetResistances.cs`, `PlayerHurtPipeline.cs`);
- update the assertion.

Never copy the new output as the expected value. List every re-pinned test in the report, with its old and new number.

- [ ] **Step 7: Drift manifest, assumptions, sync point**

- **`tests/mirrored-sources.json`:**
  - delete the entries `ARPGEnemySystem/Common/GlobalNPCs/BossManager.cs` and `.../NPCManager.cs`;
  - add `ARPGEnemySystem/Common/GlobalNPCs/EnemyProfileNPC.cs` and `ARPGEnemySystem/Common/GlobalNPCs/EnemyStats.cs`;
  - re-read each changed mirrored mod file (`PlayerHurtPipeline.cs`, `TargetResistances.cs`, `ElementalDamageCalculator.cs`, `ReapResolver.cs`, `OutgoingHitPlayer.cs`, `ProjectileManager.cs`) and confirm the BalanceLab mirror matches;
  - then run `dotnet run --project src -- mirror-hashes --write`.
- **`docs/ASSUMPTIONS.md`:**
  - delete the entries for the EoC/Plantera AI-reset simplification, the projectile halving (S11) and the Destroyer regular-enemy hit part;
  - add, phrased as approximations: "Vanilla defense reducers (Ichor, Betsy's Curse, vanilla armor pen on gear) are not modelled on the mod side; only the mod's armor-pen affixes and stats reduce defense." and "A projectile boss's biggest hit is the level multiplier applied to its vanilla final damage (contact × ProjectileDamageFactor). The mod scales the projectile's own field before vanilla's ×2 and difficulty multiplier, so the two differ only by integer truncation."
- **`BalanceLab/CLAUDE.md`:** replace the "Last sync point" table with the ES and CS commit hashes produced by Tasks 3-4 (`git -C ../ARPGEnemySystem rev-parse --short HEAD`, and the same for CS), with the date, "mirror-hashes all match", and the new test count.

- [ ] **Step 8: Full suite green**

Run `dotnet test` in `BalanceLab/`. Expected: all pass (known flake aside), including `MirrorDriftTests` and `MirrorCitationCoverageTests`.

- [ ] **Step 9: Eval round 4**

```bash
cd BalanceLab
for b in bloom-samurai bloom-samurai-allround; do for cp in eoc wof mechs plantera moonlord; do echo "################ $b @ $cp"; dotnet run --project src -- eval $b $cp --difficulty both --runs 200 2>/dev/null; done; done > reports/2026-09-29-eval-round4-scaling-refactor.txt
```

Expected: the file exists with 10 sections. Put the Expert Ceiling kill-time and hits-to-die ratio per checkpoint for both builds in the report, next to round 3's numbers from `reports/2026-09-29-eval-round3-allrounder.txt`.

- [ ] **Step 10: Commit**

```bash
git -C BalanceLab add src tests data docs/ASSUMPTIONS.md CLAUDE.md reports/2026-09-29-eval-round4-scaling-refactor.txt
git -C BalanceLab commit -m "Re-sync to read-time enemy scaling: fight profiles, no boss defense step, projectile scaling, no resistance floors; eval round 4"
```

---

### Task 7: Documentation

**Files:**
- Modify: `ARPGEnemySystem/CLAUDE.md` ("High-Level Concept", the `NPCManager.AppliesToEntity` paragraph, "Core Data Flow")
- Modify: the combat and hurt-pipeline pages under `ARPGCharacterSystem/docs/systems/` (found in step 2)

- [ ] **Step 1: Rewrite ES `CLAUDE.md`'s architecture sections**

Replace the "High-Level Concept" section with:

```markdown
### High-Level Concept

Every combat NPC gets one immutable **EnemyProfile** (`Common/Scaling/EnemyProfile.cs`) at spawn, from `EnemyProfileNPC.OnSpawn` on the server:

- **Boss fights.** A boss, or an NPC in `NPCID.Sets.ShouldBeCountedAsBoss`, starts a fight: one level roll plus the boss elemental package. Anything a fight member spawns joins the fight, directly or through a projectile it fired, and shares the same profile: parts, segments and minions alike. They get no rarity and no modifiers.
- **Everything else** is a regular enemy: a level, an **EnemyRarity** and 0–3 **EnemyModifiers**.
- **Level cap:** runs from 10 on a fresh world to 200 when every boss in the loaded mod set is dead. It is derived at runtime, not persisted.
- **Damage and defense are never written.** Every reader goes through `EnemyStats` (`Common/GlobalNPCs/EnemyStats.cs`), which scales whatever vanilla holds at that moment. Vanilla AI rewrites them mid-fight (Plantera, Skeletron, Duke, Empress every frame).
- **Only max life, coin value and size are written.** This happens at spawn and after every `NPC.SetDefaults` re-run on a live NPC: Transform, EoW splits, natural-spawn variants, client packets. There the `On_NPC.SetDefaults` detour carries the profile and the health fraction.
- **Enemy projectiles** reference their shooter's profile and scale with it.
- **Vanilla's defense step is zeroed for every enemy.** Defense acts only through the mod's physical resistance, which converts with a mirrored curve and has no floor.
```

Replace the `NPCManager.AppliesToEntity` paragraph with:

```markdown
`EnemyProfileNPC.AppliesToEntity`: `boss`, or not `townNPC` / `friendly` / `CountsAsACritter` / `TargetDummy`. The `friendly` guard is required in addition to `townNPC` because some NPCs (Skeleton Merchant, Old Man) are friendly but not flagged as town NPCs.
```

Replace the "Core Data Flow" code block with:

```
BossRoster (Boss Checklist roster + downed predicates)
       ↓ (polled once per second, server only)
WorldManager.levelCap = 10 + (190 / N) × downed
       ↓
EnemyProfileNPC.OnSpawn (server)  → fight profile (join or start) or regular profile (level + rarity + modifiers)
       ↓                            writes max life / value / size once
On_NPC.SetDefaults detour         → re-attaches the same profile after any SetDefaults re-run, keeps the health fraction
SendExtraAI / ReceiveExtraAI      → 3–9 B of profile inputs; clients rebuild, never roll
       ↓ (read at hit / tooltip / XP time)
EnemyStats.ContactDamage / Defense / ProjectileDamage → profile applied to vanilla's current raw value
EnemyProfileNPC.ModifyIncomingHit → zeroes vanilla defense (resistance replaces it)
ProjectileManager.OnSpawn         → projectile references its shooter's (or parent projectile's) profile
```

Also update the `Mod.Call` paragraph: "a fight member reports its fight's level with tier 0; `null` for an NPC `EnemyProfileNPC` doesn't apply to".

- [ ] **Step 2: Find and update the CS docs pages**

Run `grep -rln -i "armou\?r pen\|physres\|phys res\|resistance\|hurt pipeline\|NPCManager\|BossManager\|projectile damage" ARPGCharacterSystem/docs/systems`. In each listed page, bring every statement in line with the new rules:
- defense converts mirrored with no floor;
- Ichor, Betsy's Curse and vanilla armor pen now lower physical resistance;
- enemy projectiles scale with their shooter and carry vanilla's ×2 and difficulty multiplier;
- player resistances have no floor.

Write for players and designers, matching each page's existing voice.

- [ ] **Step 3: Hand the in-game pass to the user**

In the final report, list the spec's §6 in-game checklist (8 items) together with Task 3's step-9 checks. Present them as the user's merge gate. Nothing is merged or declared done until the user has run them.

- [ ] **Step 4: Commit**

```bash
git -C ARPGEnemySystem add CLAUDE.md
git -C ARPGEnemySystem commit -m "docs: read-time enemy scaling architecture"
git -C ARPGCharacterSystem add docs/systems
git -C ARPGCharacterSystem commit -m "docs: defense reducers, mirrored resistance, projectile scaling"
```
