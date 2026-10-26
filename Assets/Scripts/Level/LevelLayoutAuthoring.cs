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
        [Tooltip("What to place procedurally (props, enemy spawners) and where")]
        public LevelPlacementSet Placements;
        [Tooltip("Terrain of every corner on the edge of the grid (unless painted otherwise). Wall closes the level.")]
        public WfcCornerRule Border = WfcCornerRule.Wall;

        [Header("Generation")]
        public LevelGenerationMode Mode = LevelGenerationMode.GenerateAtRuntime;
        [Tooltip("Generate at runtime: pick a new seed every play (it's logged, so a level can be reproduced)")]
        public bool RandomSeedEachPlay = true;
        public uint Seed = 1;
        [Tooltip("Restarts with the next seed when the solver gets stuck, up to this many times")]
        [Min(1)] public int MaxAttempts = 20;

        [Header("Connectivity")]
        [Tooltip("After solving, every walkable area is connected to the player start: small pockets become wall, larger ones get a corridor carved through wall or water")]
        public bool ConnectWalkableAreas = true;
        [Tooltip("Pockets with fewer walkable corners than this are filled with wall instead of connected (unless they contain painted or forced corners)")]
        [Min(0)] public int FillPocketsSmallerThan = 6;

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
        /// <summary>What the connectivity repair changed in the preview, per corner (WfcSolver.Repair*). Not saved.</summary>
        [NonSerialized] public byte[] PreviewRepairs;
        /// <summary>Procedural placements in the preview (LevelPlacementJob). Not saved.</summary>
        [NonSerialized] public PlacementPoint[] PreviewPlacements;

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

        // World positions of the bent corners, rebuilt when the grid, bend, seed, position or turn changes
        [NonSerialized] Vector3[] _worldCorners;
        [NonSerialized] int _worldCornersKey;
        [NonSerialized] float3[] _worldCornersFrom;

        Vector3[] WorldCorners()
        {
            var bent = BentCorners();
            var position = transform.position;
            var yaw = Yaw;
            int key = HashCode.Combine(position, yaw);
            if (_worldCorners == null || key != _worldCornersKey || !ReferenceEquals(bent, _worldCornersFrom))
            {
                _worldCorners = new Vector3[bent.Length];
                for (int i = 0; i < bent.Length; i++) _worldCorners[i] = position + yaw * (Vector3)bent[i];
                _worldCornersKey = key;
                _worldCornersFrom = bent;
            }
            return _worldCorners;
        }

        /// <summary>World position of a grid point (in cells) on the bent grid with Seed, turned by Yaw around this object.</summary>
        public Vector3 GridToWorld(float x, float y)
        {
            // Blending the world corners is the same as bending, then moving and turning (both are linear)
            var corners = WorldCorners();
            int cx = Mathf.Clamp(Mathf.FloorToInt(x), 0, Width - 1), cy = Mathf.Clamp(Mathf.FloorToInt(y), 0, Height - 1);
            float fx = x - cx, fy = y - cy;
            int stride = Width + 1;
            var south = Vector3.LerpUnclamped(corners[cx + cy * stride], corners[cx + 1 + cy * stride], fx);
            var north = Vector3.LerpUnclamped(corners[cx + (cy + 1) * stride], corners[cx + 1 + (cy + 1) * stride], fx);
            return Vector3.LerpUnclamped(south, north, fy);
        }

        /// <summary>World position of a corner.</summary>
        public Vector3 CornerPosition(int x, int y)
        {
            var corners = WorldCorners();
            return corners[Mathf.Clamp(x, 0, Width) + Mathf.Clamp(y, 0, Height) * (Width + 1)];
        }

        /// <summary>World position of a cell's center.</summary>
        public Vector3 CellCenter(int x, int y) => GridToWorld(x + 0.5f, y + 0.5f);

        // Where the unbent grid puts a world position (in cells): a good first guess for picking
        Vector2 UnbentGrid(Vector3 world)
        {
            var local = Quaternion.Inverse(Yaw) * (world - transform.position);
            return new Vector2(local.x, local.z) / TileSize;
        }

        /// <summary>The cell whose bent outline contains the world position (seen from above).</summary>
        public bool TryPickCell(Vector3 world, out Vector2Int cell)
        {
            // Around the unbent guess first; the whole grid only when a strong bend moved it further
            var guess = UnbentGrid(world);
            var near = new RectInt(Mathf.FloorToInt(guess.x) - 3, Mathf.FloorToInt(guess.y) - 3, 7, 7);
            return TryPickCell(world, near, out cell) || TryPickCell(world, new RectInt(0, 0, Width, Height), out cell);
        }

        bool TryPickCell(Vector3 world, RectInt area, out Vector2Int cell)
        {
            var point = new Vector2(world.x, world.z);
            for (int y = Mathf.Max(0, area.yMin); y < Mathf.Min(Height, area.yMax); y++)
                for (int x = Mathf.Max(0, area.xMin); x < Mathf.Min(Width, area.xMax); x++)
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
            var guess = UnbentGrid(world);
            var near = new RectInt(Mathf.RoundToInt(guess.x) - 3, Mathf.RoundToInt(guess.y) - 3, 7, 7);
            return TryPickCorner(world, near, out corner) || TryPickCorner(world, new RectInt(0, 0, Width + 1, Height + 1), out corner);
        }

        bool TryPickCorner(Vector3 world, RectInt area, out Vector2Int corner)
        {
            var point = new Vector2(world.x, world.z);
            float best = TileSize * TileSize * 0.25f;
            corner = default;
            bool found = false;
            for (int y = Mathf.Max(0, area.yMin); y < Mathf.Min(Height + 1, area.yMax); y++)
                for (int x = Mathf.Max(0, area.xMin); x < Mathf.Min(Width + 1, area.xMax); x++)
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
        // The preview (or saved layout) on the bent grid, as one cached mesh per terrain: each quarter of a cell is a
        // patch in its corner's terrain color (walls raised, water lowered). Rebuilt only when what it shows changes.
        [NonSerialized] Mesh[] _previewMeshes;
        [NonSerialized] int _previewKey;

        void OnDrawGizmos()
        {
            if (!LevelLayoutView.TilePreview || TileSet == null) return;
            var cells = PreviewCells != null && PreviewCells.Length == Width * Height ? PreviewCells : SavedCells;
            int key = PreviewKey(cells);
            if (_previewMeshes == null || key != _previewKey)
            {
                BuildPreviewMeshes(cells);
                _previewKey = key;
            }

            var previousMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, Yaw, Vector3.one);
            for (int t = 0; t < _previewMeshes.Length; t++)
            {
                if (_previewMeshes[t] == null || _previewMeshes[t].vertexCount == 0) continue;
                Gizmos.color = WfcTerrains.Color((WfcTerrain)t);
                Gizmos.DrawMesh(_previewMeshes[t]);
            }
            Gizmos.matrix = previousMatrix;
        }

        void OnDisable()
        {
            if (_previewMeshes != null)
                foreach (var mesh in _previewMeshes) if (mesh != null) DestroyImmediate(mesh);
            _previewMeshes = null;
        }

        // Changes when the shown cells, pieces, tile set or bend change (the mesh is in this object's space)
        int PreviewKey(int[] cells)
        {
            int key = HashCode.Combine(cells != null ? cells.GetHashCode() : 0, TileSet.GetHashCode(), _bentCornersKey, Pieces.Count);
            foreach (var placed in Pieces) key = HashCode.Combine(key, placed.Cell, placed.Piece, placed.Rotation);
            BentCorners();
            return HashCode.Combine(key, _bentCornersKey);
        }

        void BuildPreviewMeshes(int[] cells)
        {
            var vertices = new List<Vector3>[WfcTerrains.Count];
            var indices = new List<int>[WfcTerrains.Count];
            for (int t = 0; t < WfcTerrains.Count; t++) { vertices[t] = new List<Vector3>(); indices[t] = new List<int>(); }
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
                        AddCell(x, y, corners, vertices, indices);
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
                        AddCell(placed.Cell.x + px, placed.Cell.y + py, corners, vertices, indices);
                    }
            }

            if (_previewMeshes == null) _previewMeshes = new Mesh[WfcTerrains.Count];
            for (int t = 0; t < WfcTerrains.Count; t++)
            {
                if (_previewMeshes[t] == null)
                    _previewMeshes[t] = new Mesh { name = "Level preview", hideFlags = HideFlags.HideAndDontSave, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                var mesh = _previewMeshes[t];
                mesh.Clear();
                mesh.SetVertices(vertices[t]);
                mesh.SetTriangles(indices[t], 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
            }
        }

        // The cell's 4 quarters (SW, SE, NE, NW) as patches on the bent grid, in this object's space
        void AddCell(int x, int y, WfcTerrain[] corners, List<Vector3>[] vertices, List<int>[] indices)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return;
            var bent = BentCorners();
            for (int k = 0; k < 4; k++)
            {
                float x0 = x + (k == 1 || k == 2 ? 0.5f : 0f), y0 = y + (k >= 2 ? 0.5f : 0f);
                var terrain = corners[k];
                var lift = Vector3.up * (terrain == WfcTerrain.Wall ? 3f : terrain == WfcTerrain.Water ? -0.3f : 0f);
                var list = vertices[(int)terrain];
                int start = list.Count;
                list.Add((Vector3)LevelDeformation.Sample(bent, Width, Height, x0, y0) + lift);
                list.Add((Vector3)LevelDeformation.Sample(bent, Width, Height, x0, y0 + 0.5f) + lift);
                list.Add((Vector3)LevelDeformation.Sample(bent, Width, Height, x0 + 0.5f, y0 + 0.5f) + lift);
                list.Add((Vector3)LevelDeformation.Sample(bent, Width, Height, x0 + 0.5f, y0) + lift);
                var triangles = indices[(int)terrain];
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
            }
        }
#endif
    }

#if UNITY_EDITOR
    /// <summary>What the level layout shows in the Scene view. Set from the LevelLayout Inspector (remembered in EditorPrefs).</summary>
    public static class LevelLayoutView
    {
        public const string Prefs = "PerfectLoot.LevelLayout.Show.";

        public static bool Grid, CornerRules, TilePreview, Props, Spawners, TriggerRings, Labels, Repairs;

        // Loaded on first use, so gizmos respect it even before the Inspector is opened
        static LevelLayoutView()
        {
            Grid = UnityEditor.EditorPrefs.GetBool(Prefs + "Grid", true);
            CornerRules = UnityEditor.EditorPrefs.GetBool(Prefs + "CornerRules", true);
            TilePreview = UnityEditor.EditorPrefs.GetBool(Prefs + "TilePreview", true);
            Props = UnityEditor.EditorPrefs.GetBool(Prefs + "Props", true);
            Spawners = UnityEditor.EditorPrefs.GetBool(Prefs + "Spawners", true);
            TriggerRings = UnityEditor.EditorPrefs.GetBool(Prefs + "TriggerRings", true);
            Labels = UnityEditor.EditorPrefs.GetBool(Prefs + "Labels", false);
            Repairs = UnityEditor.EditorPrefs.GetBool(Prefs + "Repairs", true);
        }
    }
#endif
}
