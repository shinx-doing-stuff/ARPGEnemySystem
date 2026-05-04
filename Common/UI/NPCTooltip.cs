using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria.ModLoader;
using Terraria;
using Terraria.UI;
using Microsoft.Xna.Framework;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Terraria.GameInput;
using Terraria.GameContent.Events;
using ARPGEnemySystem.Common.GlobalNPCs;
using ARPGEnemySystem.Common.Elements;
using ARPGEnemySystem.Common.Configs;
using Terraria.Localization;

namespace ARPGEnemySystem.Common.UI
{
    internal class NPCUI : UIState
    {
        private const string LocPrefix = "Mods.ARPGEnemySystem.NPCTooltip.";

        public override void OnInitialize()
        {

        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            RemoveAllChildren();
            var npcTooltip = new UITextPanel<string>("");
            npcTooltip.DrawPanel = false;
            Append(npcTooltip);

            // These are needed to make sure that the mouse position works correctly for every zoom level
            PlayerInput.SetZoom_Unscaled();
            PlayerInput.SetZoom_MouseInWorld();

            // Get mouse "hitbox"
            Rectangle mouseRectangle = new Rectangle((int)(Main.mouseX + Main.screenPosition.X), (int)(Main.mouseY + Main.screenPosition.Y), 1, 1);

            // This is needed to make sure that the mouse position works correctly for every UI zoom level
            PlayerInput.SetZoom_UI();

            // Loop through (hopefully) every NPC on screen and check
            for (int i = 0; i < 200; i++)
            {
                var npc = Main.npc[i];
                if (!npc.active) continue;

                // Get NPC "hitbox"
                Rectangle npcPos = new Rectangle((int)npc.Bottom.X - npc.frame.Width / 2, (int)npc.Bottom.Y - npc.frame.Height, npc.frame.Width, npc.frame.Height);

                if (!mouseRectangle.Intersects(npcPos)) continue;

                var cfg = ModContent.GetInstance<Config>();
                float physRes = ElementalMath.ConvertDefenseToResistance(
                    npc.defense, cfg.PhysResHalfPoint, cfg.ElementalResistanceCap);

                string tooltipText = null;

                if (npc.TryGetGlobalNPC<NPCManager>(out var modNpc))
                {
                    tooltipText = BuildNormalTooltip(npc, modNpc, physRes);
                }
                else if (npc.TryGetGlobalNPC<BossManager>(out var bossNpc))
                {
                    tooltipText = BuildBossTooltip(npc, bossNpc, physRes);
                }

                if (tooltipText == null) continue;

                npcTooltip.SetText(tooltipText);
                Vector2 size = FontAssets.MouseText.Value.MeasureString(tooltipText);
                npcTooltip.Width.Set(size.X + 20, 0);
                npcTooltip.Height.Set(size.Y + 20, 0);
                npcTooltip.Left.Set(Main.screenWidth / 2 - npcTooltip.Width.Pixels / 2, 0);
                npcTooltip.Top.Set(Main.screenHeight / 10, 0);
                npcTooltip.Recalculate();
                npcTooltip.DrawPanel = true;
            }
        }

        private static string BuildNormalTooltip(NPC npc, NPCManager modNpc, float physRes)
        {
            var sb = new StringBuilder();
            sb.Append(npc.GivenOrTypeName);

            string modifierList = modNpc.modifierList.Count > 0
                ? string.Join(", ", modNpc.modifierList.Select(o => o.modifierType))
                : null;

            sb.Append('\n');
            if (modifierList != null)
                sb.Append(Language.GetTextValue(LocPrefix + "HeaderNormal", modNpc.level, modNpc.rarity.rarity, modifierList));
            else
                sb.Append(Language.GetTextValue(LocPrefix + "HeaderNormalNoMods", modNpc.level, modNpc.rarity.rarity));

            AppendCommonStats(sb, npc, modNpc.FireResistance, modNpc.ColdResistance, modNpc.LightningResistance,
                              modNpc.FireDamagePct, modNpc.ColdDamagePct, modNpc.LightningDamagePct,
                              modNpc.FirePen, modNpc.ColdPen, modNpc.LightningPen, modNpc.SunderingPct, physRes);

            return sb.ToString();
        }

        private static string BuildBossTooltip(NPC npc, BossManager bossNpc, float physRes)
        {
            var sb = new StringBuilder();
            sb.Append(npc.GivenOrTypeName);

            sb.Append('\n');
            sb.Append(Language.GetTextValue(LocPrefix + "HeaderBoss", bossNpc.level));

            AppendCommonStats(sb, npc, bossNpc.FireResistance, bossNpc.ColdResistance, bossNpc.LightningResistance,
                              bossNpc.FireDamagePct, bossNpc.ColdDamagePct, bossNpc.LightningDamagePct,
                              bossNpc.FirePen, bossNpc.ColdPen, bossNpc.LightningPen, bossNpc.SunderingPct, physRes);

            return sb.ToString();
        }

        private static void AppendCommonStats(StringBuilder sb, NPC npc,
            float fireRes, float coldRes, float lightRes,
            float fireDmg, float coldDmg, float lightDmg,
            float firePen, float coldPen, float lightPen, float sunderingPct,
            float physRes)
        {
            sb.Append('\n');
            sb.Append(Language.GetTextValue(LocPrefix + "StatsLine", npc.damage, npc.defense, physRes.ToString("F1")));

            sb.Append('\n');
            sb.Append(Language.GetTextValue(LocPrefix + "Resistances",
                fireRes.ToString("F0"), coldRes.ToString("F0"), lightRes.ToString("F0")));

            AppendElemDmg(sb, fireDmg, coldDmg, lightDmg);
            AppendPen(sb, firePen, coldPen, lightPen, sunderingPct);
        }

        private static void AppendElemDmg(StringBuilder sb, float fire, float cold, float light)
        {
            bool any = fire > 0f || cold > 0f || light > 0f;
            if (!any)
            {
                sb.Append('\n').Append(Language.GetTextValue(LocPrefix + "ElemDmgNone"));
                return;
            }

            sb.Append('\n').Append(Language.GetTextValue(LocPrefix + "ElemDmgLabel"));
            if (fire  > 0f) sb.Append("  ").Append(Language.GetTextValue(LocPrefix + "Fire",      fire.ToString("F0")));
            if (cold  > 0f) sb.Append("  ").Append(Language.GetTextValue(LocPrefix + "Cold",      cold.ToString("F0")));
            if (light > 0f) sb.Append("  ").Append(Language.GetTextValue(LocPrefix + "Lightning", light.ToString("F0")));
        }

        private static void AppendPen(StringBuilder sb, float fire, float cold, float light, float sundering)
        {
            bool any = fire > 0f || cold > 0f || light > 0f || sundering > 0f;
            if (!any) return;

            sb.Append('\n').Append(Language.GetTextValue(LocPrefix + "PenLabel"));
            if (fire      > 0f) sb.Append("  ").Append(Language.GetTextValue(LocPrefix + "FirePen",      fire.ToString("F0")));
            if (cold      > 0f) sb.Append("  ").Append(Language.GetTextValue(LocPrefix + "ColdPen",      cold.ToString("F0")));
            if (light     > 0f) sb.Append("  ").Append(Language.GetTextValue(LocPrefix + "LightningPen", light.ToString("F0")));
            if (sundering > 0f) sb.Append("  ").Append(Language.GetTextValue(LocPrefix + "Sundering",    sundering.ToString("F0")));
        }
    }
}
