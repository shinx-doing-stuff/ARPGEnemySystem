using ARPGEnemySystem.Common.GlobalProjectiles;
using ARPGEnemySystem.Common.Scaling;
using Terraria;

namespace ARPGEnemySystem.Common.GlobalNPCs
{
    // The only way mod code reads enemy damage and defense: vanilla AI rewrites the raw fields
    // mid-fight, so scaling is applied on read.
    public static class EnemyStats
    {
        // Ichor and Betsy's Curse; vanilla applies them inside the defense step EnemyProfileNPC replaces.
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

        // Shooter-scaled; the hurt pipeline applies vanilla's hostile ×2 and difficulty on top.
        public static int ProjectileDamage(Projectile proj)
        {
            var p = Profile(proj);
            return p == null ? proj.damage : p.ScaleDamage(proj.damage);
        }
    }
}
