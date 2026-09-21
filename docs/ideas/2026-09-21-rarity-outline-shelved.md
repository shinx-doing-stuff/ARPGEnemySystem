# Rarity Outline on Enemy Sprites (shelved)

**Status:** shelved 2026-09-21, not implemented. Kept so the research does not have to be redone.

## The idea

Draw a coloured outline around every non-boss enemy sprite that reflects its rolled
`Rarity` (Uncommon / Rare / Elite / Legend), so a player can read an enemy's threat
before the stat panel or the first hit. Tiers would escalate: Rare a static outline,
Elite a pulsing one, Legend shimmering between two colours or carrying a faint aura.
A spawn flash on first sight was the one "reactive" extra worth keeping.

## Why it was shelved

- Terraria is already draw-bound in the moments that matter: events, boss fights, modded
  projectile and dust storms. Any per-enemy render cost lands exactly where the frame
  budget is tightest.
- The outline is cosmetic. It adds a second colour language on top of dust, debuff
  visuals, and modded VFX, and can confuse rather than inform.
- Rarity is already shown in the enemy stat panel (`ConfigClient.EnableEnemyStatPanel`).

## What was established (so it need not be re-researched)

### Feasibility: yes, vanilla does it

Terraria's pixel shader has a `ColorOnly` pass, bound as an armor shader to
`ItemID.ColorOnlyDye` and reachable via
`ContentSamples.CommonlyUsedContentSamples.ColorOnlyShaderIndex`. It draws any sprite as
a flat silhouette in one colour. Vanilla uses it for the map's outlined NPC heads
(`Main.DrawWithOutlines`, `AnOutlinedDrawRenderTargetContent`): stamp the silhouette at
offsets of 1 to 2 px in each direction, then draw the real sprite on top.

Hook: `GlobalNPC.PreDraw(npc, spriteBatch, screenPos, drawColor)` runs before the sprite
is drawn, inside the vanilla NPC batch, on all clients. Rarity is already synced, so the
feature is purely client-side with no new network traffic.

Vanilla NPC batch parameters (needed to restore the batch after a shader pass):
`SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState,
DepthStencilState.None, Main.Rasterizer, null, Main.Transform`.

Default sprite placement to replicate:
`npc.Center - screenPos + halfSize * npc.scale + (0, num3 + NPCAddHeight(npc) + npc.gfxOffY)`,
origin `halfSize` (half the frame size), `npc.rotation`, `npc.scale`, flipped by
`npc.spriteDirection`. `num3` is a per-type fudge that is 0 for almost every enemy.
Enemies with custom draw code that bypass their base sheet would get a slightly wrong
outline under any approach.

### Approach A: shader stamps per enemy

In `PreDraw`: `End()` the batch, `Begin` in Immediate mode, apply the ColorOnly shader
with the rarity colour, draw 4 to 8 offset stamps of `npc.frame`, `End()`, `Begin` the
vanilla batch again, return true. This is exactly the pattern vanilla uses for the
shimmer-transform effect in `DrawNPCDirect_Inner`.

Cost: two batch restarts per outlined enemy, each a flush plus render-state setup,
roughly 20 to 50 µs CPU. Scales linearly with outlined enemies on screen.

### Approach B: pre-baked outline textures (recommended if ever revived)

On first sight of an enemy type: `Texture2D.GetData` its sprite sheet, dilate the alpha
mask by 1 to 2 px per frame on the CPU (respecting frame boundaries from
`Main.npcFrameCount[type]`), write a white outline into a new `Texture2D` padded 2 px per
side, cache by type, dispose in `Unload`. Per frame: one extra tinted quad in the
existing batch, same transform as the sprite. Wait for `TextureAssets.Npc[type]` to be
loaded before baking; skip the outline until then.

Cost: one GPU readback stall per type per session (roughly 0.2 to 2 ms, once), 1 to 4 MB
of VRAM for the 30 to 80 types a session typically touches (vanilla's 697 sheets are
1.6 MB compressed on disk, roughly 20 to 30 MB decoded). Per frame: negligible.

### Estimated per-frame cost

Vanilla culls NPC drawing to the screen plus an 800 px margin. Spawns are capped at 5
base with stacked event multipliers of 1.3× to 3×, hard cap 200. Rare-and-up share is the
active weight column in `RarityDatabase.rarityWeightDatabase`: 10% pre-boss, 25% at Wall
of Flesh, 70% post-Plantera. Frame budget at 60 fps is 16.7 ms.

| Scenario | NPCs drawn | Rare+ share | Outlined | A: restarts | A: est. cost | B: est. cost |
|---|---|---|---|---|---|---|
| Early exploring | 8 | 10% | 1 | 2 | ~0.05 ms | ~0 |
| Hardmode surface | 12 | 45% | 5 | 10 | 0.2 to 0.5 ms | ~0 |
| Endgame event (Eclipse, Pumpkin Moon) | 40 | 70% | 28 | 56 | 1 to 3 ms | <0.05 ms |
| Pathological (200 on screen, multiplayer) | 200 | 70% | 140 | 280 | 5 to 14 ms | <0.1 ms |

Numbers are estimates from the decompile and hardware norms, not in-game measurements.

### Cost of the escalation ideas

- Colour pulse, two-tone shimmer, spawn flash: tint changes, free under both approaches.
- Additive Legend aura: needs `BlendState.Additive`, so one restart pair per Legend or one
  pair per frame if all auras are drawn in a single extra pass. Under approach B a
  scaled-up second outline quad gives a soft aura with no restart.

## If revived

Use approach B, all rarities from Uncommon up, behind a client config toggle. Give
`Rarity` a proper definition table (localisation key, colour, outline style) since the
stat panel currently prints the raw enum name and no rarity colour exists anywhere in the
mod. Measure in an Eclipse before committing to any per-frame work.
