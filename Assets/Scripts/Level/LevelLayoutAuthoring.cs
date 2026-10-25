using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace LevelGeneration
{
    public enum LevelGenerationMode
    {
        GenerateAtRuntime,  // Solve on level load (fixed seed, or a new one each play)
        UseSavedLayout,     // Spawn the layout saved from the editor preview
    }

    /// <summary>
    /// A level built by wave function collapse: a grid of Width x Height cells of TileSize meters, starting at this
    /// object's position (its south-west corner; rotation is ignored). Corner rules, forced tiles and pieces are painted
    /// in the Scene view (LevelLayoutEditor). Must be in the Entities subscene: the baker turns it into the request
    /// LevelGenerationSystem builds the level from.
    /// </summary>
    public class LevelLayoutAuthoring : MonoBehaviour
    {
        [Serializable]
        public struct ForcedTile
        {
            public Vector2Int Cell;
            public int Tile;
            public int Rotation;
        }

        [Serializable]
        public struct PlacedPiece
        {
            [Tooltip("The south-west cell of the piece's footprint")]
            public Vector2Int Cell;
            public int Piece;
            public int Rotation;
        }

        [Header("Grid")]
        [Min(1)] public int Width = 20;
        [Min(1)] public int Height = 20;
        [Tooltip("Meters per cell. Tile prefabs aren't scaled, so they must be made for this size.")]
        [Min(0.1f)] public float TileSize = 5f;
        public WfcTileSet TileSet;
        [Tooltip("Terrain of every corner on the edge of the grid (unless painted otherwise). Wall closes the level.")]
        public WfcCornerRule Border = WfcCornerRule.Wall;

        [Header("Generation")]
        public LevelGenerationMode Mode = LevelGenerationMode.GenerateAtRuntime;
        [Tooltip("Generate at runtime: pick a new seed every play (it's logged, so a level can be reproduced)")]
        public bool RandomSeedEachPlay = true;
        public uint Seed = 1;
        [Tooltip("Restarts with the next seed when the solver gets stuck, up to this many times")]
        [Min(1)] public int MaxAttempts = 20;

        [Header("Streaming")]
        [Tooltip("Chunks (8x8 cells) are built when the player comes within this many meters; keep it above every spawner's trigger + spawn radius")]
        [Min(5f)] public float LoadDistance = 60f;
        [Tooltip("Most chunks built per frame while playing (the ones around the player start are built at once)")]
        [Min(1)] public int ChunksPerFrame = 2;

        [Header("Bend (random per seed)")]
        [Tooltip("How far corners move sideways, as a fraction of the tile size")]
        [Range(0f, LevelDeformation.MaxWobble)] public float WobbleStrength;
        [Tooltip("Noise frequency per cell: lower is broader and smoother")]
        [Min(0.01f)] public float WobbleScale = 0.25f;
        [Tooltip("Degrees the whole grid curves over its width (0 = straight)")]
        [Range(-90f, 90f)] public float BendAngle;
        [Tooltip("Meters the ground rises or sinks at most. Keep slopes gentle: units walk with horizontal movement.")]
        [Min(0f)] public float HillHeight;
        [Min(0.01f)] public float HillScale = 0.15f;

        [Serializable]
        public class SpawnerMarker
        {
            public Vector2Int Cell;
            [Tooltip("Horizontal distance from the player that triggers the spawn; keep it beyond the screen edge")]
            public float TriggerRadius = 35f;
            [Tooltip("How far around the cell's center the group is scattered")]
            public float SpawnRadius = 4f;
            public List<EnemySpawner.Entry> Group = new List<EnemySpawner.Entry>();
        }

        [Header("Rules (painted in the Scene view)")]
        [Tooltip("Where the player spawns. Its 4 corners are always Walkable.")]
        public Vector2Int PlayerStart = new Vector2Int(10, 10);
        public List<ForcedTile> ForcedTiles = new List<ForcedTile>();
        public List<PlacedPiece> Pieces = new List<PlacedPiece>();
        [Tooltip("Enemy groups that rise out of the ground once, when the player first comes within TriggerRadius")]
        public List<SpawnerMarker> EnemySpawners = new List<SpawnerMarker>();

        // (Width + 1) x (Height + 1) WfcCornerRule values, row by row from the south-west
        [SerializeField, HideInInspector] byte[] _cornerRules = Array.Empty<byte>();
        [SerializeField, HideInInspector] int _cornerRulesWidth, _cornerRulesHeight;

        // Saved layout: per cell Tile * 4 + Rotation, or -1 (nothing) / -2 (covered by a piece)
        [SerializeField, HideInInspector] int[] _savedCells = Array.Empty<int>();
        [SerializeField, HideInInspector] int _savedWidth, _savedHeight;

        /// <summary>Editor preview, same format as the saved layout. Not saved with the scene.</summary>
        [NonSerialized] public int[] PreviewCells;

        public int CornerWidth => Width + 1;
        public int CornerHeight => Height + 1;

        public bool HasSavedLayout => _savedCells != null && _savedCells.Length == Width * Height &&
                                      _savedWidth == Width && _savedHeight == Height;

        public int[] SavedCells => HasSavedLayout ? _savedCells : null;

        public void SetSavedCells(int[] cells)
        {
            _savedCells = cells ?? Array.Empty<int>();
            _savedWidth = cells != null ? Width : 0;
            _savedHeight = cells != null ? Height : 0;
        }

        public static int Pack(int tile, int rotation) => tile * 4 + rotation;
        public static int PackedTile(int packed) => packed >> 2;
        public static int PackedRotation(int packed) => packed & 3;

        public WfcCornerRule GetCornerRule(int x, int y)
        {
            EnsureCornerRules();
            return (WfcCornerRule)_cornerRules[x + y * CornerWidth];
        }

        public void SetCornerRule(int x, int y, WfcCornerRule rule)
        {
            EnsureCornerRules();
            if (x < 0 || y < 0 || x >= CornerWidth || y >= CornerHeight) return;
            _cornerRules[x + y * CornerWidth] = (byte)rule;
        }

        public void ClearCornerRules()
        {
            _cornerRules = new byte[CornerWidth * CornerHeight];
            _cornerRulesWidth = CornerWidth;
            _cornerRulesHeight = CornerHeight;
        }

        /// <summary>Keeps the corner rules sized to the grid; resizing keeps rules at the same coordinates.</summary>
        public void EnsureCornerRules()
        {
            if (_cornerRules != null && _cornerRulesWidth == CornerWidth && _cornerRulesHeight == CornerHeight &&
                _cornerRules.Length == CornerWidth * CornerHeight) return;

            var resized = new byte[CornerWidth * CornerHeight];
            if (_cornerRules != null && _cornerRules.Length == _cornerRulesWidth * _cornerRulesHeight)
                for (int y = 0; y < Mathf.Min(_cornerRulesHeight, CornerHeight); y++)
                    for (int x = 0; x < Mathf.Min(_cornerRulesWidth, CornerWidth); x++)
                        resized[x + y * CornerWidth] = _cornerRules[x + y * _cornerRulesWidth];
            _cornerRules = resized;
            _cornerRulesWidth = CornerWidth;
            _cornerRulesHeight = CornerHeight;
        }

        /// <summary>The grid's turn: only the Transform's Y rotation, so the level always stays flat.</summary>
        public Quaternion Yaw => Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        /// <summary>The bend settings for a seed (the runtime uses the solve seed; the editor uses Seed).</summary>
        public LevelDeformation Deformation(uint seed) => new LevelDeformation
        {
            Width = Width, Height = Height, TileSize = TileSize, Seed = seed,
            WobbleStrength = WobbleStrength, WobbleScale = WobbleScale, BendAngle = BendAngle,
            HillHeight = HillHeight, HillScale = HillScale,
        };

        // Bent corners for Seed (grid space), rebuilt when a setting changes
        [NonSerialized] float3[] _bentCorners;
        [NonSerialized] int _bentCornersKey;

        public float3[] BentCorners()
        {
            int key = HashCode.Combine(Width, Height, TileSize, Seed, WobbleStrength, WobbleScale, BendAngle, HashCode.Combine(HillHeight, HillScale));
            if (_bentCorners == null || key != _bentCornersKey)
            {
                _bentCorners = Deformation(Seed).BuildCorners();
                _bentCornersKey = key;
            }
            return _bentCorners;
        }

        /// <summary>World position of a grid point (in cells) on the bent grid with Seed, turned by Yaw around this object.</summary>
        public Vector3 GridToWorld(float x, float y) =>
            transform.position + Yaw * (Vector3)LevelDeformation.Sample(BentCorners(), Width, Height, x, y);

        /// <summary>World position of a corner.</summary>
        public Vector3 CornerPosition(int x, int y) => GridToWorld(x, y);

        /// <summary>World position of a cell's center.</summary>
        public Vector3 CellCenter(int x, int y) => GridToWorld(x + 0.5f, y + 0.5f);

        /// <summary>The cell whose bent outline contains the world position (seen from above).</summary>
        public bool TryPickCell(Vector3 world, out Vector2Int cell)
        {
            var point = new Vector2(world.x, world.z);
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    var a = Flat(CornerPosition(x, y));
                    var b = Flat(CornerPosition(x + 1, y));
                    var c = Flat(CornerPosition(x + 1, y + 1));
                    var d = Flat(CornerPosition(x, y + 1));
                    if (InTriangle(point, a, b, c) || InTriangle(point, a, c, d))
                    {
                        cell = new Vector2Int(x, y);
                        return true;
                    }
                }
            cell = default;
            return false;
        }

        /// <summary>The bent corner nearest the world position (seen from above), within half a tile.</summary>
        public bool TryPickCorner(Vector3 world, out Vector2Int corner)
        {
            var point = new Vector2(world.x, world.z);
            float best = TileSize * TileSize * 0.25f;
            corner = default;
            bool found = false;
            for (int y = 0; y <= Height; y++)
                for (int x = 0; x <= Width; x++)
                {
                    float distance = (Flat(CornerPosition(x, y)) - point).sqrMagnitude;
                    if (distance >= best) continue;
                    best = distance;
                    corner = new Vector2Int(x, y);
                    found = true;
                }
            return found;
        }

        static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
            bool negative = d1 < 0f || d2 < 0f || d3 < 0f;
            bool positive = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(negative && positive);
        }

        static float Cross(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);

        void OnValidate()
        {
            EnsureCornerRules();
            PlayerStart = new Vector2Int(Mathf.Clamp(PlayerStart.x, 0, Width - 1), Mathf.Clamp(PlayerStart.y, 0, Height - 1));
        }

#if UNITY_EDITOR
        // The preview (or saved layout) on the bent grid: each quarter of a cell is a patch in its corner's terrain
        // color (walls raised, water lowered)
        void OnDrawGizmos()
        {
            var cells = PreviewCells != null && PreviewCells.Length == Width * Height ? PreviewCells : SavedCells;
            if (TileSet == null) return;
            var corners = new WfcTerrain[4];

            if (cells != null)
                for (int y = 0; y < Height; y++)
                    for (int x = 0; x < Width; x++)
                    {
                        int packed = cells[x + y * Width];
                        if (packed < 0) continue;
                        int tile = PackedTile(packed);
                        if (tile >= TileSet.Tiles.Count || TileSet.Tiles[tile] == null) continue;
                        WfcRotation.RotateCorners(TileSet.Tiles[tile].Corners, PackedRotation(packed), corners);
                        DrawCell(x, y, corners);
                    }

            // Pieces are rules, so they show with or without a preview
            foreach (var placed in Pieces)
            {
                if (placed.Piece < 0 || placed.Piece >= TileSet.Pieces.Count) continue;
                var piece = TileSet.Pieces[placed.Piece];
                piece.EnsureCorners();
                var grid = WfcRotation.RotateGrid(piece.Corners, piece.Size.x, piece.Size.y, placed.Rotation, out int w, out int h);
                for (int py = 0; py < h; py++)
                    for (int px = 0; px < w; px++)
                    {
                        corners[0] = grid[px + py * (w + 1)];
                        corners[1] = grid[px + 1 + py * (w + 1)];
                        corners[2] = grid[px + 1 + (py + 1) * (w + 1)];
                        corners[3] = grid[px + (py + 1) * (w + 1)];
                        DrawCell(placed.Cell.x + px, placed.Cell.y + py, corners);
                    }
            }
        }

        void DrawCell(int x, int y, WfcTerrain[] corners)
        {
            var points = new Vector3[4];
            for (int k = 0; k < 4; k++)
            {
                // Quarter k: SW, SE, NE, NW half of the cell in each direction
                float x0 = x + (k == 1 || k == 2 ? 0.5f : 0f), y0 = y + (k >= 2 ? 0.5f : 0f);
                var terrain = corners[k];
                var lift = Vector3.up * (terrain == WfcTerrain.Wall ? 3f : terrain == WfcTerrain.Water ? -0.3f : 0f);
                points[0] = GridToWorld(x0, y0) + lift;
                points[1] = GridToWorld(x0 + 0.5f, y0) + lift;
                points[2] = GridToWorld(x0 + 0.5f, y0 + 0.5f) + lift;
                points[3] = GridToWorld(x0, y0 + 0.5f) + lift;
                UnityEditor.Handles.color = WfcTerrains.Color(terrain);
                UnityEditor.Handles.DrawAAConvexPolygon(points);
            }
        }
#endif
    }
}
