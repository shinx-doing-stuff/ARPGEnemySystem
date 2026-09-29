using System.IO;
using ARPGEnemySystem.Common.GlobalNPCs;
using ARPGEnemySystem.Common.Scaling;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace ARPGEnemySystem.Common.GlobalProjectiles
{
    public class ProjectileManager : GlobalProjectile
    {
        public override bool InstancePerEntity => true;

        // The shooter's (or parent projectile's) profile; outlives the shooter. Null when no enemy fired it.
        public EnemyProfile Profile;

        // Only for the kaeshi counter-strike, never for scaling.
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
}
