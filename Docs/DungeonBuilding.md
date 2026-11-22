# Building a dungeon

How a Perfect Loot dungeon level is put together, every lever there is to shape one, and what building the first Blender
biome (Abyssal Ruins) taught us. For the look of things (palettes, shaders, shape language) see
[ArtStyleGuide.md](ArtStyleGuide.md) sections 11 to 14.

## 1. The pieces

| What | Asset / code | Decides |
|---|---|---|
| Dungeon | `DungeonDefinition` (Assets/Scripts/Dungeons) | Steps, time limit, loot, experience, difficulty scaling |
| Level layout | `LevelLayoutAuthoring` in the test scene's Entities subscene | Grid size, tile set, painted rules, ring, backdrop, generation, bend |
| Tile set | `WfcTileSet` (one per biome) | Corner and edge connections, tiles and their weights, pieces |
| Placement set | `LevelPlacementSet` | Props, edge props and enemy spawners placed by seed |
| Backdrop | `LevelBackdrop` (one per biome) | What lies beyond the level: sinking ring, abyss fade, mist, silhouettes |
| Biome palette | `BiomeTileSets.All` (Editor/Level/BiomeTileSets.cs) | Colors, light, fog, post-processing, ambient particles |
| Tile models | `ArtSource/Tools/pl_wfc_kit.py` in `ArtSource/BiomeKit.blend` | The kit's shapes, connections, tile list and weights |
| Backdrop models | `ArtSource/Tools/pl_backdrop.py` | Silhouette pieces standing in the abyss |

Every machine builds the same level from the seed (solve, ring, looks, landmarks, placement, backdrop), so nothing about
the level's shape is sent over the network. The runtime log prints a hash of the solved cells to compare host and clients.

## 2. Levers

### Dungeon (`DungeonDefinition`)
- **Steps**, done in order; the last finishes the dungeon:
  - Kill Enemies: a share of the enemies the level's spawners make.
  - Kill Boss: spawned at a painted Boss Spawn Point.
  - Reach Exit: a painted Dungeon Exit. Dying sends the player back to the start.
  - Survive: seconds to stay alive while waves come (wave interval, wave groups by weight, spawn distance and radius,
    a cap on living enemies).
- **Time Limit**, **Kill Loot Table** (every kill), **Completion Loot Table** and **Completion Experience**.
- **Extra Enemies Per Difficulty** and **Max Enemy Multiplier**: how difficulty scales enemy counts.
- **Return Portal**: spawned when the dungeon is finished.

### Level layout (`LevelLayoutAuthoring`)
- **Grid:** Width, Height and Tile Size (5 m; tile models are made for it). Border: what the grid's edge is (Not
  Walkable closes the level).
- **Painted rules** (Scene view brushes): corner rules (Walkable, Not Walkable, Water or one named connection), Player
  Start, Forced Tiles, Pieces, Enemy Spawners (trigger radius, spawn radius, group), Prefabs (portals, exits, chests;
  each reserves a radius), Landmarks (random pieces placed by seed: min and max count, spacing).
- **Background:** Background Ring (cells of scenery around the level, 5 with a backdrop), Background Border and
  Backdrop.
- **Generation:** Mode (generate at runtime or use the saved layout), Random Seed Each Play, Seed, Max Attempts.
- **Connectivity:** Connect Walkable Areas (carves corridors or fills pockets so everything reaches the start) and Fill
  Pockets Smaller Than.
- **Streaming:** Load Distance (chunks of 8x8 cells build within it; keep it above every spawner's trigger plus spawn
  radius) and Chunks Per Frame.
- **Bend (per seed):** Wobble Strength and Scale (corners move sideways), Bend Angle (the whole grid curves), Hill
  Height and Scale (gentle rises; units move horizontally, so keep it small).

### Tile set (`WfcTileSet`)
- **Corner connections:** what a corner can be. Each has a gameplay Terrain (Walkable, Not Walkable, Water), a Height
  (meters; a tile's model is raised to its lowest corner) and Higher (the same connection one step up).
- **Edge connections:** what an edge can be, and whether units can walk across it (Plain and Slope yes, Cliff no).
- **Tiles:** four corner Ids and four edge Ids, Weight (how often the solver picks it among tiles that fit), Allow
  Rotations, Allow Mirror, Walk Through (units cross all its walkable corners, even over a cliff edge: a narrow ramp),
  Background (only in the ring) and Looks (other models for the same tile, picked per cell so neighbours differ).
- **Pieces:** multi-cell set pieces (arenas, shrines) with their own corner grid, placed by hand or as landmarks.
- **Foundation Depth:** how deep every model must reach below its lowest corner.

### Placement set (`LevelPlacementSet`)
- Kinds: Prop (on the floor, random yaw), Edge Prop (on grid edges along chosen edge connections or where walkable
  ground ends, facing the walkable side) and Enemy Spawner (groups by weight).
- Per rule: Spacing, clearance from walls and water, distance from the player start, steepest ground for spawners,
  trigger and spawn radius. Rules only use flat cells on ground reachable from the start.

### Backdrop (`LevelBackdrop`)
- **Ring Sink:** meters the ring curves down at its outer edge (15).
- **Abyss Color, Fade Start, Fade Depth, Fade Bands:** everything below the level fades in hard steps to one color;
  the sky below the horizon uses it too.
- **Mist:** a prefab of sheets at several depths. Each sheet's material (`PerfectLoot/Mist`) has opacity, noise
  scale, coverage, bands, drift and brightness.
- **Pieces:** silhouette prefabs, Piece Count, Piece Distance (meters outside the ring), Piece Depth (meters below
  the ground for a piece's top), Piece Scale and Piece Spacing.

### Biome kit (`pl_wfc_kit.py`)
- `STYLES[biome]`: seed, wall height and jitter, drop depth, slab size, cliff jitter, accent kind, glow, spires, and
  factors for how much liquid and drop appear.
- `CONNECTIONS` and `EDGES`: the kit's connections. The high ground is the same kinds 1.25 m up (`HIGH`).
  Abyssal Ruins was exported at 2.5 m and keeps that until the kit is exported again. Don't go below about 1 m: units
  step over anything under 0.4 m, and a ledge just above that blocks walking while looking like a curb.
- `WEIGHT` per connection (a tile's weight is the product over its corners), plus per-tile factors in `kit_tiles`:
  corner and diagonal cliffs are rarer than straight ones, and ramps use `RAMP_WEIGHT`.
- `kit_tiles` is the tile list: which pairs of connections get transition tiles, junctions, bridges, ramps (narrow,
  side, middle), looks, mirroring and the Background tiles.
- `RAMP_HALF` (narrow ramp width), `LIP` (stone lip along cliff tops and chasm rims).

### Biome palette (`BiomeTileSets.Biome`)
- Floor, wall, rock, accent and liquid colors; chasm rim, fog and mist; key light, ambient trilight, fog color and
  density; bloom, saturation, contrast, vignette, shadow and highlight tints; ambient particle kind.
- `Tools/Level/Biomes/Build All Biome Tile Sets` rebuilds every tile set (from the Blender kit when a biome has one).
  `Tools/Level/Biomes/Apply Biomes To Test Levels` puts each biome on its test level: light, fog, sky, volume,
  particles, tile set, ring and backdrop.

## 3. Making a new biome with a Blender kit

1. Add the biome to `BiomeTileSets.All` (palette) and a `STYLES` entry in `pl_wfc_kit.py`.
2. In Blender (`ArtSource/BiomeKit.blend`): `K.build_kit(K.STYLES[name], collection)`, look at it, then
   `K.export_kit(objects, "Assets/Data/Level/Biomes/<Biome>/Models", style)`.
3. Backdrop pieces: `BD.build_backdrop(collection)` and `BD.export_backdrop(objects, ".../Models/Backdrop")`.
4. In Unity: Build All Biome Tile Sets, then Apply Biomes To Test Levels (or set the level's Tile Set, ring and
   Backdrop by hand).
5. Check it (section 5).

## 4. What Abyssal Ruins taught us

**Exporting models**
- Unity's FBX import flips X, so every Blender model came out mirrored east-west, and tiles didn't match the corners
  the solver placed them by. No axis setting can undo a mirror. Export only through `pl_wfc_tiles.export_fbx`, which
  mirrors in X while writing and restores the Blender objects after. Other Blender exports (characters, equipment)
  still use the raw exporter and may be mirrored too.
- Blender renders and solver-only checks all looked right while the game was wrong. Only measuring the imported Unity
  mesh (where an asymmetric tile's drop or wall vertices sit against its corner names) showed the mirror.

**Modelling tiles**
- A face whose outline passes through a grid point gets that point twice, and bmesh refuses the face: holes at almost
  every seam. `Builder.face` drops repeated points.
- Shade anything that fades by depth with an absolute measure (depth below ground 0), never relative to the tile's own
  rim, or neighbouring tiles shade differently.
- Skirts under sloped slabs must follow the slope, or ramps look stepped.
- A stone lip along cliff tops and chasm rims made edges read much better; only put it where the neighbouring
  connection is lower.
- Rock walls need the cliff's color: a near-black side made the background rock look like floating plates.

**Tile sets and the solver**
- Connections, not terrain, decide what fits. The terrain only says how a corner plays.
- Ramps: a narrow ramp is a Walk Through tile with a straight cliff's edges. Wide ramps (RampSide and RampMid with a
  walkable Slope edge that only matches another Slope) must not be Walk Through: the connectivity repair asks for Walk
  Through tiles, and a wide ramp in a carved corridor can't get the ramp neighbours it needs, so exits became
  unreachable.
- Lowering ramp weights to get fewer of them cut the exit off in 2 to 4 of 20 seeds. Check reachability over many
  seeds after any weight change.
- Corner and diagonal cliffs at lower weight make cliffs run long instead of zigzagging.
- Without junction tiles (cliff meeting a chasm) a plateau could only be ringed by all cliffs or all drop, and a high
  area beside a chasm could never get a ramp down.
- Carve corridors before filling small pockets, and never fill a corridor's corner.

**Beyond the level**
- A flat background ring only moves the hard edge outward: the camera (38° pitch, 60° field of view) looks almost to
  the horizon. The edge has to fall away instead: sink the ring, fade the depths to one color that matches the sky
  below the horizon, add mist and silhouettes.
- The fade is applied after the distance fog, so deep down everything is exactly the abyss color and no edge shows.
- Backdrop pieces have their pivot at the top, so a depth setting can never make one rise over the level and block the
  camera.
- Global shader values outlive the level. Clear them when the level goes away (`OnStopRunning`) and when a level
  without a backdrop builds.

**Testing**
- Chunks only stream in near the player, so a camera moved far from the player shows only the backdrop. Take
  screenshots near the player, or move the player.
- Play mode freezes when the editor isn't focused, and background play drops keyboard and mouse. Driving it through
  the unity CLI with run in background on works; turn it off afterwards, since it edits project settings.
- Rebuilding the kit should leave existing tile prefabs unchanged; a diff there means the export changed.

## 5. Checking a level

- **Many seeds:** solve 20 seeds and count unreachable exits, pockets left, and walled ramp neighbours. Every change
  to weights or tiles can shift these.
- **Same as before:** compare the cells hash of other levels per seed, so a change to one biome doesn't move another.
- **Imported meshes:** after a new export, measure an asymmetric tile in Unity against its corner names.
- **Play mode:** screenshots at the start, at a side edge and at a corner near the start, at the gameplay camera.
- **Look:** walkable ground, the drop and props must read at a glance (ArtStyleGuide section 7).
