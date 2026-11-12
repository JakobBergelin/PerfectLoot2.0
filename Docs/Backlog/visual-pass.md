# Visual pass backlog (night of 2026-11-12)

Branch `art/visual-pass`. The risky part is on `art/visual-pass-hitstop` (one commit on top of `art/visual-pass` as it was then).

## What changed

- **Icons:** 42 new icons drawn with the existing kit (`ArtSource/Tools/Icons/VisualPassIcons.cs`, run `make_icons.ps1`): 15 active skills, 5 passives, the Execute power, 6 status effects (`Images/EffectIcons`), 10 classes (`Images/ClassIcons`) and 5 resources (`Images/ResourceIcons`). Buffs that come from a power or passive reuse its icon (Momentum, Prism, Shadowstep, Refraction uses Refractionist, Stolen Souls uses Soul Thief, Risen uses Raise Dead, Thrall Mark uses Raise Thrall, Invulnerable uses Invulnerability). No asset under `Assets/Data` has an empty Icon now.
- **Descriptions:** the ten classes and Gale Ward had none.
- **Biome tile sets:** see `Docs/ArtStyleGuide.md` section 14. Test Kill Enemies, Test Survive, Test Kill Boss and Test Reach Exit use one biome each now (tile set, light, ambient, fog, sky, post-processing, ambient particles).
- **Haunted Cathedral** uses `CathedralTileSet` (it used the gray placeholder tiles). Hub, Haunted Cathedral, Test Arena and Test Swarm got the fog-colored sky.
- **Game camera post-processing is on** (`GameRig.prefab`, MainCamera): it was off, so no bloom, vignette or tonemapping ever showed in game. SMAA is on too.
- **Strike effects** (`VfxBuilder.Strikes.cs`, `VfxSystems.Strike`): a player's single-frame shaped attack gets a slash arc (cone), thrust streak (rectangle) or ground shockwave (round, 2 m or more), tinted by damage type, at most 12 per frame. Cold rectangles (Frost Lance) no longer show the round Cold Snap nova.
- **Projectile streaks:** physical and lightning projectiles had no effect (Multishot, Cursed Hammer, enemy arrows). They get a tinted dart with a trail and a spark or lightning burst at the end.
- **Skill bar:** a slot pops (scale and white border, 260 ms) when its cooldown ends.

## Branch `art/visual-pass-hitstop`

`HitStopSystem`: time nearly stops (time scale 0.04) for 25-100 ms when a skill with Crit Shake crits, longer for more shake. Risky because `Time.timeScale` is global (co-op players all freeze for anyone's crit) and it changes game time. Compiled, not seen in play. Only skills with Crit Shake above 0 trigger it (Cold Snap 0.5, Ball Lightning 0.35, Frozen Orb 0.3, Tornado 0.3, Hookshot Slash 0.25), so raise Crit Shake on a skill to try it there.

## Seen in play

- All four biome levels, in screenshots at the gameplay camera (`Docs/screenshots/Biome_*.png`): walls, lava, water, chasms, glow pools, floor textures, mushrooms, crystals, embers, snow, motes, rune circles, puddles.
- Strike effects spawned by hand in slow motion (fire slash, physical shockwave, cold thrust).
- Hub and Haunted Cathedral with post-processing on.

## Not seen working

- Strike effects and projectile streaks from real skill casts: no skill was cast (background play drops keyboard input). Try Swipe, Upheaval, Meteor, Frost Lance and Multishot in Test Arena.
- The skill bar's ready pop: compiled, not seen.
- New icons in the Character Select class list: class icons aren't shown anywhere in the UI yet, only filled in on the assets.
- Hit-stop (see above).
- Frame rate with the biome tiles in Test Swarm-sized levels. A tile is about 1,150 vertices on average (placeholder tiles were a few dozen), and chunks merge 8 x 8 tiles.

## Open items and ideas

- Abyss walls still read as boulders rather than ruined masonry. A wall style with straight courses and broken tops would sell "ruins" better.
- Biome floors are one texture per tile with a seam on each border, so the 5 m grid shows on big open floors. The rarer floor variant with a feature helps; more variants (and a third floor texture) would help more.
- Many enemy bodies are a beige that sits close to the Ember and Mire floor hues. Section 11's rule (enemies 2x brighter than the floor, in a hue the floor lacks) isn't met for the skeletons there.
- The comic shader's point lights reach about 2.4 m (`world.md`), which is why liquids light the floor with `PerfectLoot/Glow` bands instead of lights.
- Ball Lightning, Sonic Ball and the Timezone debug shape (from `juice.md`) are still not done.
- `BiomeTileSets.ApplyToScene` rewrites the test scenes' lighting each run, so hand edits to those settings are lost if it runs again.
- Mesh containers (`<Biome>_Meshes.asset`, about 5 MB each, text) could be made binary or the tile resolution lowered (`BiomeTileGenerator.N`, 20 samples per side) if repo size matters.

## How to go back

- One test level: set its LevelLayout's Tile Set back to `PlaceholderTileSet` in its Entities subscene, and its scene lighting by hand.
- Post-processing off: untick Post Processing on MainCamera in `GameRig.prefab`.
