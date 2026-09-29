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
using ARPGEnemySystem.Common.Scaling;
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

            float physResHalfPoint = ModContent.GetInstance<Config>().PhysResHalfPoint;

            // Loop through (hopefully) every NPC on screen and check
            for (int i = 0; i < 200; i++)
            {
                var npc = Main.npc[i];
                if (!npc.active) continue;

                // Get NPC "hitbox"
                Rectangle npcPos = new Rectangle((int)npc.Bottom.X - npc.frame.Width / 2, (int)npc.Bottom.Y - npc.frame.Height, npc.frame.Width, npc.frame.Height);

                if (!mouseRectangle.Intersects(npcPos)) continue;

                var profile = EnemyStats.Profile(npc);
                if (profile == null) continue;

                string tooltipText = BuildTooltip(npc, profile, physResHalfPoint);

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

        private static string BuildTooltip(NPC npc, EnemyProfile p, float physResHalfPoint)
        {
            var sb = new StringBuilder();
            sb.Append(npc.GivenOrTypeName);
            sb.Append('\n');
            if (p.Kind == EnemyKind.FightMember)
                sb.Append(Language.GetTextValue(LocPrefix + "HeaderBoss", p.Level));
            else if (p.Modifiers.Length > 0)
            {
                var modifierNames = new StringBuilder();
                for (int i = 0; i < p.Modifiers.Length; i++)
                {
                    if (i > 0)
                        modifierNames.Append(", ");
                    modifierNames.Append(p.Modifiers[i].modifierType);
                }
                sb.Append(Language.GetTextValue(LocPrefix + "HeaderNormal", p.Level, p.Rarity, modifierNames.ToString()));
            }
            else
                sb.Append(Language.GetTextValue(LocPrefix + "HeaderNormalNoMods", p.Level, p.Rarity));

            // What a hit sees: scaled defense less Ichor / Betsy's Curse.
            int defense = EnemyStats.Defense(npc) - EnemyStats.DebuffDefenseReduction(npc);
            float physRes = ElementalMath.ConvertDefenseToResistance(
                defense, physResHalfPoint, ElementalMath.ElementCap);

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

        private static void AppendElemDmg(StringBuilder sb, float fire, float cold, float light, float chaos)
        {
            bool any = fire > 0f || cold > 0f || light > 0f || chaos > 0f;
            if (!any)
            {
                sb.Append('\n').Append(Language.GetTextValue(LocPrefix + "ElemDmgNone"));
                return;
            }

            sb.Append('\n').Append(Language.GetTextValue(LocPrefix + "ElemDmgLabel"));
            if (fire  > 0f) sb.Append("  ").Append(Language.GetTextValue(LocPrefix + "Fire",      fire.ToString("F0")));
            if (cold  > 0f) sb.Append("  ").Append(Language.GetTextValue(LocPrefix + "Cold",      cold.ToString("F0")));
            if (light > 0f) sb.Append("  ").Append(Language.GetTextValue(LocPrefix + "Lightning", light.ToString("F0")));
            if (chaos > 0f) sb.Append("  ").Append(Language.GetTextValue(LocPrefix + "Chaos",     chaos.ToString("F0")));
        }

        private static void AppendPen(StringBuilder sb, float fire, float cold, float light, float sundering, float chaos)
        {
            bool any = fire > 0f || cold > 0f || light > 0f || sundering > 0f || chaos > 0f;
            if (!any) return;

            sb.Append('\n').Append(Language.GetTextValue(LocPrefix + "PenLabel"));
            if (fire      > 0f) sb.Append("  ").Append(Language.GetTextValue(LocPrefix + "FirePen",      fire.ToString("F0")));
            if (cold      > 0f) sb.Append("  ").Append(Language.GetTextValue(LocPrefix + "ColdPen",      cold.ToString("F0")));
            if (light     > 0f) sb.Append("  ").Append(Language.GetTextValue(LocPrefix + "LightningPen", light.ToString("F0")));
            if (sundering > 0f) sb.Append("  ").Append(Language.GetTextValue(LocPrefix + "Sundering",    sundering.ToString("F0")));
            if (chaos     > 0f) sb.Append("  ").Append(Language.GetTextValue(LocPrefix + "ChaosPen",     chaos.ToString("F0")));
        }
    }
}
