using System;
using System.Collections.Generic;
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

        /// <summary>World position of a grid point (in cells) at this object's height, turned by Yaw around this object.</summary>
        public Vector3 GridToWorld(float x, float y) => transform.position + Yaw * new Vector3(x * TileSize, 0f, y * TileSize);

        /// <summary>Grid coordinates (in cells) of a world position, ignoring height.</summary>
        public Vector2 WorldToGrid(Vector3 world)
        {
            var local = Quaternion.Inverse(Yaw) * (world - transform.position);
            return new Vector2(local.x, local.z) / TileSize;
        }

        /// <summary>World position of a corner.</summary>
        public Vector3 CornerPosition(int x, int y) => GridToWorld(x, y);

        /// <summary>World position of a cell's center.</summary>
        public Vector3 CellCenter(int x, int y) => GridToWorld(x + 0.5f, y + 0.5f);

        void OnValidate()
        {
            EnsureCornerRules();
            PlayerStart = new Vector2Int(Mathf.Clamp(PlayerStart.x, 0, Width - 1), Mathf.Clamp(PlayerStart.y, 0, Height - 1));
        }

#if UNITY_EDITOR
        // The preview (or saved layout) as colored blocks: each quarter of a cell shows its corner's terrain
        void OnDrawGizmos()
        {
            var cells = PreviewCells != null && PreviewCells.Length == Width * Height ? PreviewCells : SavedCells;
            if (TileSet == null) return;
            var corners = new WfcTerrain[4];
            // Drawn in grid space, turned with the layout
            var previousMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, Yaw, Vector3.one);

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
            Gizmos.matrix = previousMatrix;
        }

        // In grid space (Gizmos.matrix places the grid)
        void DrawCell(int x, int y, WfcTerrain[] corners)
        {
            var center = new Vector3((x + 0.5f) * TileSize, 0f, (y + 0.5f) * TileSize);
            float quarter = TileSize * 0.25f;
            for (int k = 0; k < 4; k++)
            {
                var offset = new Vector3(k == 1 || k == 2 ? quarter : -quarter, 0f, k >= 2 ? quarter : -quarter);
                var terrain = corners[k];
                Gizmos.color = WfcTerrains.Color(terrain);
                var size = new Vector3(TileSize * 0.5f, 0.2f, TileSize * 0.5f);
                var position = center + offset + Vector3.down * 0.1f;
                if (terrain == WfcTerrain.Wall) { size.y = 3f; position.y = center.y + 1.5f; }
                else if (terrain == WfcTerrain.Water) position.y = center.y - 0.3f;
                Gizmos.DrawCube(position, size);
            }
        }
#endif
    }
}
