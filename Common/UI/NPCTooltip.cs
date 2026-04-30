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
using Terraria.GameContent.UI.Elements;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Terraria.GameInput;
using Terraria.GameContent.Events;
using ARPGEnemySystem.Common.GlobalNPCs;
using ARPGEnemySystem.Common.Elements;
using ARPGEnemySystem.Common.Configs;

namespace ARPGEnemySystem.Common.UI
{
    internal class NPCUI : UIState
    {
        //public NPCTooltip npcTooltip;

        public override void OnInitialize()
        {

        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            RemoveAllChildren();
            var npcTooltip = new UITextPanel<string>("");
            npcTooltip.DrawPanel=false;
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

                if (mouseRectangle.Intersects(npcPos))
                {
                    var cfg = ModContent.GetInstance<Config>();
                    float physRes = ElementalMath.ConvertDefenseToResistance(
                        npc.defense, cfg.DefenseToPhysResRatio, cfg.ElementalResistanceCap);

                    NPCManager modNpc;
                    BossManager bossNpc;
                    if (npc.TryGetGlobalNPC<NPCManager>(out modNpc))
                    {
                        string elemDmgLine = modNpc.ElementalDamageType == Element.Physical
                            ? "Elem Dmg: none"
                            : $"Elem Dmg: {modNpc.ElementalDamageType} {modNpc.ElementalDamagePct:F0}%";

                        string tooltipText = npc.GivenOrTypeName +
                                            $"\nLevel: {modNpc.level} " +
                                            $"\nRarity: {modNpc.rarity.rarity} " +
                                            $"\nModifier: {String.Join(", ", modNpc.modifierList.Select(o => o.modifierType).ToList())}" +
                                            $"\nDefense: {npc.defense}" +
                                            $"\nPhys Res: {physRes:F1}%" +
                                            $"\nFire Res: {modNpc.FireResistance:F1}%" +
                                            $"\nCold Res: {modNpc.ColdResistance:F1}%" +
                                            $"\nLightning Res: {modNpc.LightningResistance:F1}%" +
                                            $"\n{elemDmgLine}";
                        npcTooltip.SetText(tooltipText);
                        npcTooltip.Width.Set(npcTooltip.TextSize.X + 20, 0);
                        npcTooltip.Height.Set(240, 0);
                        npcTooltip.Left.Set(Main.screenWidth / 2 - npcTooltip.Width.Pixels / 2, 0);
                        npcTooltip.Top.Set(Main.screenHeight / 10, 0);
                        npcTooltip.Recalculate();
                        npcTooltip.DrawPanel = true;
                    }
                    if (npc.TryGetGlobalNPC<BossManager>(out bossNpc))
                    {
                        string elemDmgLine = bossNpc.ElementalDamageType == Element.Physical
                            ? "Elem Dmg: none"
                            : $"Elem Dmg: {bossNpc.ElementalDamageType} {bossNpc.ElementalDamagePct:F0}%";

                        string tooltipText = npc.GivenOrTypeName +
                                            $"\nLevel: {bossNpc.level} " +
                                            $"\nDefense: {npc.defense}" +
                                            $"\nPhys Res: {physRes:F1}%" +
                                            $"\nFire Res: {bossNpc.FireResistance:F1}%" +
                                            $"\nCold Res: {bossNpc.ColdResistance:F1}%" +
                                            $"\nLightning Res: {bossNpc.LightningResistance:F1}%" +
                                            $"\n{elemDmgLine}";
                        npcTooltip.SetText(tooltipText);
                        npcTooltip.Width.Set(npcTooltip.TextSize.X + 20, 0);
                        npcTooltip.Height.Set(210, 0);
                        npcTooltip.Left.Set(Main.screenWidth / 2 - npcTooltip.Width.Pixels / 2, 0);
                        npcTooltip.Top.Set(Main.screenHeight / 10, 0);
                        npcTooltip.Recalculate();
                        npcTooltip.DrawPanel = true;
                    }

                }
            }
        }
    }
}
