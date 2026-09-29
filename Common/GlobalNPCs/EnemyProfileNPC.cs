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
    // Owns each combat NPC's EnemyProfile. Only lifeMax, value and scale are written; damage and
    // defense are scaled at read time by EnemyStats.
    public class EnemyProfileNPC : GlobalNPC
    {
        public override bool InstancePerEntity => true;

        public EnemyProfile Profile;

        // Vanilla's values, recorded after each SetDefaults.
        private int _baseLifeMax;
        private float _baseValue;
        private float _baseScale;

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
                ?? NewProfileFor(npc);

            RecordBase(npc);
            ApplyWrites(npc);
            npc.life = npc.lifeMax;
            BossPlayerScaling.Announce(npc, _baseLifeMax, Profile.Level);
        }

        // Anything a fight member spawns, directly or through its projectile, joins the fight.
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

        private static EnemyProfile NewProfileFor(NPC npc)
        {
            if (npc.boss || NPCID.Sets.ShouldBeCountedAsBoss[npc.type])
                return NewFightProfile();
            return NewRegularProfile();
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
            _baseScale = npc.scale;
        }

        private void ApplyWrites(NPC npc)
        {
            npc.lifeMax = Math.Max(1, Profile.ScaleLifeMax(_baseLifeMax));
            npc.value = Profile.Kind == EnemyKind.Regular
                ? _baseValue * Utils.GetCoinMultiplier(new EnemyRarity(Profile.Rarity), Profile.Level, Profile.Modifiers.Length)
                : _baseValue;
            if (Profile.ScaleOverride is float scale)
                npc.scale = scale;
            else
                npc.scale = _baseScale;
        }

        // Server only: carries the profile across SetDefaults re-runs on a live NPC (Transform, EoW
        // split, spawner variants). A fresh spawn is still inactive here.
        private static void SetDefaultsHook(On_NPC.orig_SetDefaults orig, NPC self, int type, NPCSpawnParams spawnparams)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                orig(self, type, spawnparams);
                return;
            }

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
                // A live NPC that just became a managed type gets its first profile.
                carried = NewProfileFor(self);
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
            // Vanilla restores full life from the pre-scaling lifeMax before this is read; this bit
            // lets the client redo it after scaling.
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

        // Defense acts only through the mod's physical resistance; vanilla's bonus for negative
        // defense is cancelled too.
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
