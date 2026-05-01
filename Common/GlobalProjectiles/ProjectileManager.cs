using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria;
using Terraria.ModLoader;
using ARPGEnemySystem.Common.GlobalNPCs;
using log4net.Core;
using Terraria.ModLoader.IO;
using System.IO;

namespace ARPGEnemySystem.Common.GlobalProjectiles
{
    public class ProjectileManager : GlobalProjectile
    {
        public NPC sourceNPC;
        public NPCManager modNPC;
        public BossManager modBossNPC;
        public int npcIndex;
        public override bool InstancePerEntity => true;

        public override void OnSpawn(Projectile projectile, IEntitySource source)
        {
            if (source is EntitySource_Parent parent && parent.Entity is NPC npc)
            {
                npcIndex = npc.whoAmI;

                if (npc.TryGetGlobalNPC<NPCManager>(out modNPC))
                {
                    foreach (var modifier in modNPC.modifierList)
                    {
                        switch (modifier.modifierType)
                        {
                            case ModifierType.Strong:
                                projectile.damage += (int)(projectile.damage * modifier.magnitude / 100f);
                                break;
                        }
                    }
                }
                else
                {
                    npc.TryGetGlobalNPC<BossManager>(out modBossNPC);
                }
            }
        }

        public override void OnHitPlayer(Projectile projectile, Player target, Player.HurtInfo info)
        {
            if (modNPC != null)
            {
                foreach (var modifier in modNPC.modifierList)
                {
                    switch (modifier.modifierType)
                    {
                        case ModifierType.SoulDrinker:
                            target.statMana -= modifier.magnitude;
                            break;
                    }
                }
            }
        }

        public override void SendExtraAI(Projectile projectile, BitWriter bitWriter, BinaryWriter binaryWriter)
        {
            binaryWriter.Write7BitEncodedInt(npcIndex);
        }

        public override void ReceiveExtraAI(Projectile projectile, BitReader bitReader, BinaryReader binaryReader)
        {
            npcIndex = binaryReader.Read7BitEncodedInt();
            sourceNPC = Main.npc[npcIndex];
            sourceNPC.TryGetGlobalNPC<NPCManager>(out modNPC);
            sourceNPC.TryGetGlobalNPC<BossManager>(out modBossNPC);
        }
    }
}
