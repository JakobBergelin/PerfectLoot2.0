# Art style: comic-book dark fantasy ("dark fairytale" palette)

Rules for any art work in Perfect Loot: models, materials, scenes, prompts. Full write-up with screenshots: `Docs/ArtStyleGuide.md`.
The root `CLAUDE.md` rules still apply (show a plan and wait for approval before changing files).

## Pillars
1. **Readability first.** Silhouette → color → glow. Every enemy's trait must read in a 1-second glance at gameplay zoom (top-down, ~50° pitch, FOV 35–40). Never judge in close-up.
2. **One shader.** Everything opaque uses `PerfectLoot/Comic` (`Shaders/PL_Comic.shader`). Keep textures flat or very simple; the shader provides the style.
3. **Biomes change palette and shapes, never the trait language.**
4. **Floors stay quiet, actors pop.** Floors are dark and low-contrast; enemies are at least 2× brighter than the floor and use a hue the floor doesn't have.
5. **Serious, not grimdark.** Dark values and ink, with a few rich jewel-tone accents. Menace comes from silhouettes, not gore.

## Shape language
- Hunched, angular, jagged silhouettes; slab armor; tattered cloth; spikes.
- Chunky and exaggerated (big hands, heads, weapons). No thin, floating or fiddly parts that vanish at gameplay zoom.
- Each enemy needs one strong silhouette idea that reads before any color does.

## Trait colors (identical in every biome; a trait owns its color)
| Trait | Shape cue | Base / emission |
|---|---|---|
| Fire | cracks, ember vents | `#FF8A2A` / `#FF7A1A` |
| Frost | crystal growths | `#BFF3FF` / `#7FE6FF` |
| Poison | bulbous sacs, drips | `#9CFF4A` / `#8CFF3A` |
| Physical armor | thick plates, bulky shoulders | `#8A8F99`–`#A8AEBA`, no glow, strong rim |
| Magic shield | floating rune plates, halo ring | `#C9A2FF` / `#B57BFF` |
| Projectile reflect | mirror shell on front | `#E9F1FA`, white rim |
| Fast | thin long limbs, forward lean | none |
| Summoner | staff, totem or bell; orbiting minions | `#FFE08A` / `#FFD24A` |
| Elite | 1.25–1.3× scale | `Elite_` material copies: outline `#FF6A10`, 5 px |

Never use these colors at high saturation on environment pieces. Examples: no bright green moss in a poison biome, no gold candles near summoners, and lava stays deep red-orange (`#FF5418` / `#FF4A1C`), not yellow.

## Palette rules ("dark fairytale")
- Dark values, high contrast; saturation only in 1–3 accents per biome.
- Free accent hues (no trait owns them): **rose/pink, teal, ruby, sapphire, warm candle**.
- Lighting: warm key light + cool/teal ambient and fog. Shadow tints: characters `#5E5480`, player `#7C78A6`, environment biome-specific (cool/teal).
- Ink `#120C16`, character/prop outlines `#0B080D`.
- Floors: all floor colors within ~10% value of each other; floor outline `#1A1218` (cathedral `#14111A`).
- Enemy bodies: lighter than the floor, in a hue the floor lacks; rim strength 0.9.
- Colors are set on Unity materials. Blender colors are only a starting point, so recolor in Unity without re-exporting.

## Biome palettes (current values)
| | Ember Forest | Toxic Swamp | Haunted Cathedral |
|---|---|---|---|
| Floor | slabs `#3C2E3A`, gaps `#2E2430`, pebbles `#463C4A`, roots `#3A2832` (× 0.85) | mud `#3E3436`, moss `#3A4438`, water `#22363C`, stones `#4A4650`, roots `#36292E` (× 0.85) | stone `#44404E`, dark stone `#24222C` |
| Props | rock `#5E5A72`, ruin `#7E7892`, bark `#4A2C36`, rose cinderleaf `#C8447E` / glow `#FF5FA8` | bark `#3E2E38`, stone `#6C6A7C`, rose mushroom caps `#C8447E` / glow `#FF5FA8`, pools `#1C4A54` | stone `#8C8698`, wood `#5C3E30`, wax `#F2E4CA`, glass ruby `#B83A54`/`#FF4A6E`, sapphire `#2A4A9E`/`#4A7CFF` |
| Enemy bodies | brute `#8A7E8E`, imp `#C87A8A`, shaman robe `#B03050` | toad `#5E7C46` + belly `#E2C88E`, crawler `#8A8070`, shaman cloak `#4A8A7E` | revenant armor `#A8AEBA` + cloth `#6E2A44`, wraith `#4E6A9A` |
| Key light | `#FFD8B0` × 1.35 | `#F2DDB0` × 1.25 | moon `#B4C2F2` × 1.15 |
| Ambient sky / equator / ground | `#4E6274` / `#2E3A48` / `#141418` | `#56707A` / `#344A4C` / `#1A1E24` | `#4C4A6E` / `#2E2C42` / `#16141C` |
| Fog / background | `#1A2630` | `#20363C` | `#1E1A2C` |
| Point lights | lava `#FF5A28` | pools teal `#4FD6C8`, mushrooms `#FF6AB0` (soft) | candles `#FFA850`, glass `#FF4A6E` / `#4A7CFF` |
| Env shadow tint | `#2E4252` | `#2C4450` | `#3C3458` |

New biomes: choose a floor hue family, then 1–3 free accent hues, then check that no enemy body shares the floor hue.

## Material settings (`PerfectLoot/Comic`)
| | Characters | Environment | Glowing parts | Floors (`Ground_` copies) |
|---|---|---|---|---|
| `_OutlineWidth` | 4 | 3 | 2.5 | 1 |
| `_PatternStrength` (hatch) | 0.85–0.9 | 0.85 | **0** | 0.4 |
| `_PatternScale` / angle | 5 / 40 | 5 / 40 | — | 8 / 25 |
| `_RimStrength` | 0.9 (rim `#F2E6DA`) | 0.2 | — | 0.08 |
| `_AmbientStrength` | 0.42 | 0.32 | | |

Other settings: `_ShadeThreshold` 0.62, `_DeepThreshold` 0.38, `_OutlineJitter` 0.55. Emission: set `_EmissionMap` to white, color × 2.2 (soft environment glow × 0.9), trait pulse 0.3. Floors never cast shadows.

## Pipeline (Blender → Unity)
- Model in Blender with `ArtSource/Tools/pl_blender.py` (`import pl_blender as B`): `mat`, `prim`, `skin`, `strand`, `jag`, `lumpy`, `finalize` (join, origin at bottom center, UVs, faces sorted by material), `decimate`, `export_collection` (FBX + `slots.txt` + `materials.txt`), `preview`.
- Sources: `ArtSource/EmberForest_ArtTest.blend` (Ember Forest comic models), `ArtSource/ComicBiomes_ArtTest.blend` (swamp, cathedral, floors).
- Export: FBX `axis_forward='-Z'`, `axis_up='Y'`, `bake_space_transform`. Blender front (−Y) maps to Unity +Z. Sort faces by material right before export (slot order = submesh order).
- Models go in `Assets/ArtStyleTest/Models/<Biome>/`. The postprocessor bakes smoothed outline normals into UV3; set `materialImportMode = None`.
- Materials: one per name in `Materials/<Biome>/`, plus `Ground_` copies for floors and `Elite_` copies. Prefabs in `Prefabs/<Biome>/` assign materials explicitly in `slots.txt` order.
- Scale: 1 unit = 1 m, player ≈ 1.8 m, pivot at the feet. Budgets: enemies 1.5k–6k tris, bosses ≤ 15k, props 50–1.5k, 12 m floor tile ≤ 4k.
- AI-generated assets (Gemini → Meshy/Tripo): always clean up in Blender (decimate, pivot, scale, simplify materials) before import.

## Checklist: new enemy
1. Pick the trait(s), then the shape cue and trait color from the table.
2. Body color: brighter than the floor, in a hue the floor lacks, and not a trait color.
3. Glowing trait parts: no hatching, emission on.
4. Make an `Elite_` variant.
5. Test in the biome scene at the gameplay camera, and at a 300 px thumbnail.

## Checklist: new biome
1. Floor hue family and floor detail (raised pieces, close values, 1 px outline).
2. 1–3 free accent hues for props and lights.
3. Warm key light + cool ambient and fog; set the env shadow tint.
4. Check that no enemy body or prop competes with trait colors.
5. Build `ArtStyleTest_<Biome>_Comic` with the standard layout and camera, capture it, and add it to `Docs/screenshots/ComicBiomes.png`.

## Known open items
- The player is the weakest read in every scene. It needs a brighter, unique hue (probably a cool light blue/white) and stronger rim.
- The cathedral stained glass blooms too much.
