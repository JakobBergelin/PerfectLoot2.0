using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace LevelGeneration
{
    /// <summary>A layout's rules turned into solver input. Built the same way by the baker and the editor preview.</summary>
    public class WfcCompiledLayout
    {
        public int Width, Height;
        public List<WfcVariant> Variants = new List<WfcVariant>();
        /// <summary>(Width + 1) * (Height + 1) corner connection masks.</summary>
        public ulong[] CornerMasks;
        /// <summary>Edge connection masks, as WfcSolver.InitialEdgeMasks.</summary>
        public ulong[] EdgeMasks;
        /// <summary>Corner connections that are Walkable / NotWalkable, edge connections units can walk across (bits by index).</summary>
        public ulong WalkableCorners, NotWalkableCorners, WalkableEdges;
        /// <summary>The terrain and ground height of each corner connection, by index.</summary>
        public byte[] CornerTerrains = new byte[0];
        public float[] CornerHeights = new float[0];
        /// <summary>1 for each edge connection units can walk across, by index.</summary>
        public byte[] EdgeWalkables = new byte[0];
        /// <summary>Per cell: WfcSolver.Free, WfcSolver.Occupied or a forced variant index.</summary>
        public int[] InitialCells;
        /// <summary>Per corner: 1 when the connectivity repair may not change it (painted, piece, forced tile, start, walkable prefab, border).</summary>
        public byte[] LockedCorners;
        /// <summary>The player start cell's south-west corner: walkable areas connect to it.</summary>
        public int StartCorner = -1;
        public List<Placement> Pieces = new List<Placement>();
        public List<string> Warnings = new List<string>();
        /// <summary>
        /// The background ring (LevelLayoutAuthoring.BackgroundRing), or null: a grid Margin cells bigger on every side,
        /// solved after the level from the Background tiles. The level's cells are taken; WfcLayoutCompiler.FixRingBorder sets
        /// its border from the solved level before the ring is solved.
        /// </summary>
        public WfcCompiledLayout Ring;
        public int Margin;

        public struct Placement
        {
            public int Piece, Rotation;
            public Vector2Int Cell, Size;   // South-west cell and footprint after rotation
        }

        /// <summary>8 connection indices per variant, as WfcSolver.VariantSlots.</summary>
        public byte[] VariantSlots()
        {
            var slots = new byte[Variants.Count * WfcSolver.SlotsPerCell];
            for (int v = 0; v < Variants.Count; v++)
                for (int k = 0; k < 4; k++)
                {
                    slots[v * WfcSolver.SlotsPerCell + k] = (byte)Variants[v].Corners[k];
                    slots[v * WfcSolver.SlotsPerCell + 4 + k] = (byte)Variants[v].Edges[k];
                }
            return slots;
        }

        public float[] VariantWeights()
        {
            var weights = new float[Variants.Count];
            for (int v = 0; v < weights.Length; v++) weights[v] = Variants[v].Weight;
            return weights;
        }

        /// <summary>A solve job for this layout, with every array allocated (TempJob). Free them with job.DisposeArrays().</summary>
        public WfcSolveJob CreateJob(uint seed, int maxAttempts, bool connect, int fillPocketsSmallerThan) => new WfcSolveJob
        {
            Width = Width,
            Height = Height,
            VariantSlots = new NativeArray<byte>(VariantSlots(), Allocator.TempJob),
            VariantWeights = new NativeArray<float>(VariantWeights(), Allocator.TempJob),
            InitialCornerMasks = new NativeArray<ulong>(CornerMasks, Allocator.TempJob),
            InitialEdgeMasks = new NativeArray<ulong>(EdgeMasks, Allocator.TempJob),
            InitialCells = new NativeArray<int>(InitialCells, Allocator.TempJob),
            Seed = seed,
            MaxAttempts = maxAttempts,
            Connect = connect,
            StartCorner = StartCorner,
            FillPocketsSmallerThan = fillPocketsSmallerThan,
            WalkableCorners = WalkableCorners,
            NotWalkableCorners = NotWalkableCorners,
            WalkableEdges = WalkableEdges,
            LockedCorners = new NativeArray<byte>(LockedCorners, Allocator.TempJob),
            Result = new NativeArray<int>(Width * Height, Allocator.TempJob),
            Attempts = new NativeReference<int>(Allocator.TempJob),
            Repairs = new NativeArray<byte>(CornerMasks.Length, Allocator.TempJob),
            UnconnectedPockets = new NativeReference<int>(Allocator.TempJob),
            Backtracks = new NativeReference<int>(Allocator.TempJob),
        };

        /// <summary>Solver output (variant indices) to the saved-layout format (Tile * 4 + Rotation, or Occupied).</summary>
        public int[] ToPackedCells(int[] variantCells)
        {
            var packed = new int[variantCells.Length];
            for (int i = 0; i < packed.Length; i++)
            {
                int v = variantCells[i];
                packed[i] = v >= 0 && v < Variants.Count ? LevelLayoutAuthoring.Pack(Variants[v].Tile, Variants[v].Rotation) : v;
            }
            return packed;
        }
    }

    public static class WfcLayoutCompiler
    {
        // Keeps only the walkable connections on a cell's 4 corners and locks them, unless a piece covers the cell (then the piece decides)
        static void ForceWalkable(WfcCompiledLayout compiled, Vector2Int cell, int cornerWidth, string what)
        {
            if (compiled.InitialCells[cell.x + cell.y * compiled.Width] == WfcSolver.Occupied) return;
            for (int k = 0; k < 4; k++)
            {
                int cx = cell.x + (k == 1 || k == 2 ? 1 : 0), cy = cell.y + (k >= 2 ? 1 : 0);
                int corner = cx + cy * cornerWidth;
                ulong walkable = compiled.CornerMasks[corner] & compiled.WalkableCorners;
                if (walkable == 0ul)
                {
                    compiled.Warnings.Add($"{what} overrides the corner rule at ({cx}, {cy}) with Walkable.");
                    walkable = compiled.WalkableCorners;
                }
                compiled.CornerMasks[corner] = walkable;
                compiled.LockedCorners[corner] = 1;
            }
        }

        public static WfcCompiledLayout Compile(LevelLayoutAuthoring layout)
        {
            var compiled = new WfcCompiledLayout { Width = layout.Width, Height = layout.Height };
            int cornerWidth = layout.CornerWidth, cornerHeight = layout.CornerHeight;
            compiled.CornerMasks = new ulong[cornerWidth * cornerHeight];
            compiled.EdgeMasks = new ulong[layout.Width * cornerHeight + cornerWidth * layout.Height];
            compiled.InitialCells = new int[layout.Width * layout.Height];
            compiled.LockedCorners = new byte[cornerWidth * cornerHeight];
            for (int i = 0; i < compiled.InitialCells.Length; i++) compiled.InitialCells[i] = WfcSolver.Free;

            var tileSet = layout.TileSet;
            if (tileSet == null)
            {
                compiled.Warnings.Add("No tile set.");
                return compiled;
            }
            tileSet.EnsureConnections();
            compiled.Variants = tileSet.BuildVariants(compiled.Warnings);
            compiled.WalkableCorners = tileSet.CornerMask(WfcTerrain.Walkable);
            compiled.NotWalkableCorners = tileSet.CornerMask(WfcTerrain.NotWalkable);
            compiled.WalkableEdges = tileSet.WalkableEdges();
            compiled.CornerTerrains = tileSet.CornerTerrains();
            compiled.CornerHeights = tileSet.CornerHeights();
            compiled.EdgeWalkables = tileSet.EdgeWalkables();
            ulong allEdges = tileSet.AllEdges();
            for (int i = 0; i < compiled.EdgeMasks.Length; i++) compiled.EdgeMasks[i] = allEdges;

            // Painted rules win over the border
            ulong borderMask = tileSet.CornerMask(layout.Border);
            for (int y = 0; y < cornerHeight; y++)
                for (int x = 0; x < cornerWidth; x++)
                {
                    var rule = layout.GetCornerRule(x, y);
                    int connection = layout.GetCornerConnection(x, y);
                    bool painted = rule != WfcCornerRule.Any || connection >= 0;
                    bool edge = x == 0 || y == 0 || x == cornerWidth - 1 || y == cornerHeight - 1;
                    if (edge || painted) compiled.LockedCorners[x + y * cornerWidth] = 1;
                    compiled.CornerMasks[x + y * cornerWidth] = painted ? tileSet.CornerMask(rule, connection)
                                                              : edge ? borderMask : tileSet.CornerMask(WfcCornerRule.Any);
                    if (painted && compiled.CornerMasks[x + y * cornerWidth] == 0ul)
                        compiled.Warnings.Add($"The corner rule at ({x}, {y}) allows no connection of the tile set; the level can't be solved.");
                }

            // Pieces: their corners are fixed and their cells taken
            foreach (var placed in layout.Pieces)
            {
                if (placed.Piece < 0 || placed.Piece >= tileSet.Pieces.Count || tileSet.Pieces[placed.Piece] == null)
                {
                    compiled.Warnings.Add($"Piece at {placed.Cell} doesn't exist in the tile set.");
                    continue;
                }
                var piece = tileSet.Pieces[placed.Piece];
                piece.EnsureCorners();
                var grid = WfcRotation.RotateGrid(piece.CornerIds, piece.Size.x, piece.Size.y, placed.Rotation, out int w, out int h);
                if (!FitsAndFree(compiled, placed.Cell, w, h))
                {
                    compiled.Warnings.Add($"{piece.Name} at {placed.Cell} is outside the grid or overlaps another piece; skipped.");
                    continue;
                }
                if (System.Array.Exists(grid, id => tileSet.CornerIndex(id) < 0 || tileSet.CornerIndex(id) >= WfcConnections.Max))
                {
                    compiled.Warnings.Add($"{piece.Name} uses a corner connection the tile set doesn't have; skipped.");
                    continue;
                }
                for (int py = 0; py < h; py++)
                    for (int px = 0; px < w; px++)
                        compiled.InitialCells[placed.Cell.x + px + (placed.Cell.y + py) * layout.Width] = WfcSolver.Occupied;
                for (int py = 0; py <= h; py++)
                    for (int px = 0; px <= w; px++)
                    {
                        int corner = placed.Cell.x + px + (placed.Cell.y + py) * cornerWidth;
                        ulong mask = WfcConnections.Bit(tileSet.CornerIndex(grid[px + py * (w + 1)]));
                        if ((compiled.CornerMasks[corner] & mask) == 0ul)
                            compiled.Warnings.Add($"{piece.Name} at {placed.Cell} overrides a corner rule at {placed.Cell + new Vector2Int(px, py)}.");
                        compiled.CornerMasks[corner] = mask;
                        compiled.LockedCorners[corner] = 1;
                    }
                compiled.Pieces.Add(new WfcCompiledLayout.Placement
                {
                    Piece = placed.Piece, Rotation = ((placed.Rotation % 4) + 4) % 4,
                    Cell = placed.Cell, Size = new Vector2Int(w, h),
                });
            }

            // Player start: always floor (unless a piece is there; then the piece decides)
            var start = layout.PlayerStart;
            if (layout.InGrid(start)) ForceWalkable(compiled, start, cornerWidth, "The player start");
            // Walkable areas connect to the start (under a piece too, through the piece's corners)
            if (layout.InGrid(start))
                compiled.StartCorner = start.x + start.y * cornerWidth;

            // Hand-placed prefabs: the same, when they ask for it
            foreach (var placed in layout.Prefabs)
            {
                if (placed == null) continue;
                if (placed.Prefab == null)
                    compiled.Warnings.Add($"The placed prefab at {placed.Cell} has no prefab; skipped.");
                else if (!layout.InGrid(placed.Cell))
                    compiled.Warnings.Add($"{placed.Prefab.name} at {placed.Cell} is outside the grid; skipped.");
                else if (placed.ForceWalkable)
                    ForceWalkable(compiled, placed.Cell, cornerWidth, $"{placed.Prefab.name} at {placed.Cell}");
            }

            // Forced tiles: the variant of that tile with the matching rotation
            var tileCorners = new int[4];
            var tileEdges = new int[4];
            var corners = new int[4];
            var edges = new int[4];
            foreach (var forced in layout.ForcedTiles)
            {
                if (forced.Cell.x < 0 || forced.Cell.y < 0 || forced.Cell.x >= layout.Width || forced.Cell.y >= layout.Height) continue;
                int cell = forced.Cell.x + forced.Cell.y * layout.Width;
                if (compiled.InitialCells[cell] == WfcSolver.Occupied)
                {
                    compiled.Warnings.Add($"Forced tile at {forced.Cell} is under a piece; skipped.");
                    continue;
                }
                if (forced.Tile < 0 || forced.Tile >= tileSet.Tiles.Count || tileSet.Tiles[forced.Tile] == null)
                {
                    compiled.Warnings.Add($"Forced tile at {forced.Cell} doesn't exist in the tile set.");
                    continue;
                }
                var tile = tileSet.Tiles[forced.Tile];
                for (int k = 0; k < 4; k++)
                {
                    tileCorners[k] = tileSet.CornerIndex(tile.Corners[k]);
                    tileEdges[k] = tileSet.EdgeIndex(tile.Edges[k]);
                }
                int rotation = ((forced.Rotation % 4) + 4) % 4;
                WfcRotation.RotateCorners(tileCorners, rotation, corners);
                WfcRotation.RotateCorners(tileEdges, rotation, edges);
                var wanted = new int4(corners[0], corners[1], corners[2], corners[3]);
                var wantedEdges = new int4(edges[0], edges[1], edges[2], edges[3]);
                int variant = compiled.Variants.FindIndex(v => v.Tile == forced.Tile && math.all(v.Corners == wanted) && math.all(v.Edges == wantedEdges));
                if (variant < 0)
                {
                    compiled.Warnings.Add($"Forced tile {tile.Name} at {forced.Cell} uses a rotation the tile doesn't allow (or a missing connection).");
                    continue;
                }
                compiled.InitialCells[cell] = variant;

                for (int k = 0; k < 4; k++)
                {
                    int cx = forced.Cell.x + (k == 1 || k == 2 ? 1 : 0), cy = forced.Cell.y + (k >= 2 ? 1 : 0);
                    compiled.LockedCorners[cx + cy * cornerWidth] = 1;
                    if ((compiled.CornerMasks[cx + cy * cornerWidth] & WfcConnections.Bit(corners[k])) == 0ul)
                        compiled.Warnings.Add($"Forced tile at {forced.Cell} doesn't match the corner rule at ({cx}, {cy}); the level can't be solved.");
                }
            }

            if (layout.BackgroundRing > 0) compiled.Ring = CompileRing(layout, tileSet, compiled);
            return compiled;
        }

        static WfcCompiledLayout CompileRing(LevelLayoutAuthoring layout, WfcTileSet tileSet, WfcCompiledLayout level)
        {
            int margin = layout.BackgroundRing, w = layout.Width + 2 * margin, h = layout.Height + 2 * margin;
            var ring = new WfcCompiledLayout { Width = w, Height = h, Margin = margin, Variants = tileSet.BuildVariants(level.Warnings, background: true) };
            if (ring.Variants.Count == 0)
            {
                level.Warnings.Add("Background Ring is on, but the tile set has no Background tiles, so there's no ring.");
                return null;
            }
            ring.WalkableCorners = level.WalkableCorners;
            ring.NotWalkableCorners = level.NotWalkableCorners;
            ring.WalkableEdges = level.WalkableEdges;

            ulong any = tileSet.CornerMask(WfcCornerRule.Any), outside = tileSet.CornerMask(layout.BackgroundBorder);
            ring.CornerMasks = new ulong[(w + 1) * (h + 1)];
            for (int y = 0; y <= h; y++)
                for (int x = 0; x <= w; x++)
                    ring.CornerMasks[x + y * (w + 1)] = x == 0 || y == 0 || x == w || y == h ? outside : any;
            ring.EdgeMasks = new ulong[w * (h + 1) + (w + 1) * h];
            ulong allEdges = tileSet.AllEdges();
            for (int i = 0; i < ring.EdgeMasks.Length; i++) ring.EdgeMasks[i] = allEdges;
            ring.InitialCells = new int[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool inLevel = x >= margin && y >= margin && x < margin + layout.Width && y < margin + layout.Height;
                    ring.InitialCells[x + y * w] = inLevel ? WfcSolver.Occupied : WfcSolver.Free;
                }
            ring.LockedCorners = new byte[ring.CornerMasks.Length];
            return ring;
        }

        /// <summary>
        /// Sets the ring's corners and edges along the level's border to what the solved level has there, so the ring meets
        /// it exactly. Masks are the ring's (its CornerMasks and EdgeMasks, copied); level is the solved level's surface.
        /// </summary>
        public static void FixRingBorder(int width, int height, int margin, ulong[] ringCorners, ulong[] ringEdges, LevelSurface level)
        {
            int ringWidth = width + 2 * margin, ringHeight = height + 2 * margin;
            int cornerWidth = width + 1, across = width * (height + 1), ringAcross = ringWidth * (ringHeight + 1);
            for (int y = 0; y <= height; y++)
                for (int x = 0; x <= width; x++)
                {
                    if (x > 0 && x < width && y > 0 && y < height) continue;
                    int connection = level.CornerConnection[x + y * cornerWidth];
                    if (connection < WfcConnections.Max) ringCorners[x + margin + (y + margin) * (ringWidth + 1)] = WfcConnections.Bit(connection);
                }
            // East-west edges along the south and north sides, north-south ones along the west and east sides
            for (int x = 0; x < width; x++)
                for (int side = 0; side < 2; side++)
                {
                    int y = side * height, connection = level.EdgeConnection[x + y * width];
                    if (connection < WfcConnections.Max) ringEdges[x + margin + (y + margin) * ringWidth] = WfcConnections.Bit(connection);
                }
            for (int y = 0; y < height; y++)
                for (int side = 0; side < 2; side++)
                {
                    int x = side * width, connection = level.EdgeConnection[across + x + y * cornerWidth];
                    if (connection < WfcConnections.Max) ringEdges[ringAcross + x + margin + (y + margin) * (ringWidth + 1)] = WfcConnections.Bit(connection);
                }
        }

        static bool FitsAndFree(WfcCompiledLayout compiled, Vector2Int cell, int w, int h)
        {
            if (cell.x < 0 || cell.y < 0 || cell.x + w > compiled.Width || cell.y + h > compiled.Height) return false;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (compiled.InitialCells[cell.x + x + (cell.y + y) * compiled.Width] == WfcSolver.Occupied) return false;
            return true;
        }
    }
}
