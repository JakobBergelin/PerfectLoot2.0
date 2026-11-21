using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace LevelGeneration
{
    /// <summary>
    /// The tiles and multi-cell pieces a level is built from. Each tile names a corner connection for its 4 corners and an
    /// edge connection for its 4 edges; two tiles fit side by side when what they share matches. Corners are ordered
    /// SouthWest, SouthEast, NorthEast, NorthWest and edges South, East, North, West (+X is east, +Z is north). Prefabs
    /// have their pivot in the middle of their footprint and are rotated around Y in 90° steps.
    /// </summary>
    [CreateAssetMenu(fileName = "TileSet", menuName = "Level/WFC Tile Set")]
    public class WfcTileSet : ScriptableObject
    {
        [Tooltip("What tile corners can be. Tiles store the Id.")]
        public List<WfcCornerConnection> CornerConnections = new List<WfcCornerConnection>();
        [Tooltip("What tile edges can be. Tiles store the Id.")]
        public List<WfcEdgeConnection> EdgeConnections = new List<WfcEdgeConnection>();
        public List<WfcTile> Tiles = new List<WfcTile>();
        [Tooltip("Larger pieces (e.g. a 2x2 arena) placed by hand in the level layout")]
        public List<WfcPiece> Pieces = new List<WfcPiece>();
        [Tooltip("Meters below its lowest corner that every tile model must reach, so lower ground next to it or the drop around " +
                 "the level never shows a gap. The Inspector lists tiles that don't. 0 turns the check off.")]
        [Min(0f)] public float FoundationDepth;

        // 1: corner connections have Higher (older assets load it as 0, which would name connection 0)
        [SerializeField, HideInInspector] int _version;

        void OnEnable() => EnsureConnections();
        void OnValidate() => EnsureConnections();

        /// <summary>
        /// Every tile set has at least one corner and one edge connection, and every Id is used once. A tile set made before
        /// connections existed stored each corner's terrain (0 Walkable, 1 not walkable, 2 water) where the Id is now, so it
        /// gets three connections with those Ids and terrains, plus a Plain edge with Id 0 (what every tile's edges hold).
        /// </summary>
        public void EnsureConnections()
        {
            if (CornerConnections.Count == 0)
            {
                CornerConnections.Add(new WfcCornerConnection { Id = 0, Name = "Floor", Terrain = WfcTerrain.Walkable, Color = WfcTerrains.Color(WfcTerrain.Walkable) });
                CornerConnections.Add(new WfcCornerConnection { Id = 1, Name = "Wall", Terrain = WfcTerrain.NotWalkable, Color = WfcTerrains.Color(WfcTerrain.NotWalkable) });
                CornerConnections.Add(new WfcCornerConnection { Id = 2, Name = "Water", Terrain = WfcTerrain.Water, Color = WfcTerrains.Color(WfcTerrain.Water) });
            }
            if (EdgeConnections.Count == 0)
                EdgeConnections.Add(new WfcEdgeConnection { Id = 0, Name = "Plain", Walkable = true });
            if (_version < 1)
            {
                foreach (var connection in CornerConnections) if (connection != null) connection.Higher = -1;
                _version = 1;
            }

            // A connection added in the Inspector copies the last one, Id included
            var used = new HashSet<int>();
            foreach (var connection in CornerConnections)
                if (connection != null && !used.Add(connection.Id)) used.Add(connection.Id = FreeId(used));
            used.Clear();
            foreach (var connection in EdgeConnections)
                if (connection != null && !used.Add(connection.Id)) used.Add(connection.Id = FreeId(used));

            foreach (var piece in Pieces) piece?.MigrateCorners();
        }

        static int FreeId(HashSet<int> used)
        {
            int id = 0;
            while (used.Contains(id)) id++;
            return id;
        }

        /// <summary>The corner connection's place in CornerConnections (its bit in the solver's masks), or -1.</summary>
        public int CornerIndex(int id) => CornerConnections.FindIndex(c => c != null && c.Id == id);

        public int EdgeIndex(int id) => EdgeConnections.FindIndex(c => c != null && c.Id == id);

        public WfcCornerConnection CornerConnection(int id) => CornerConnections.Find(c => c != null && c.Id == id);

        /// <summary>The corner connections a painted rule allows: those with its terrain, or only the given Id (-1: any).</summary>
        public ulong CornerMask(WfcCornerRule rule, int connectionId = -1)
        {
            ulong mask = 0ul;
            for (int i = 0; i < CornerConnections.Count && i < WfcConnections.Max; i++)
            {
                var connection = CornerConnections[i];
                if (connection == null || !WfcTerrains.Allows(rule, connection.Terrain)) continue;
                if (connectionId >= 0 && connection.Id != connectionId) continue;
                mask |= WfcConnections.Bit(i);
            }
            return mask;
        }

        /// <summary>Every corner connection with this terrain.</summary>
        public ulong CornerMask(WfcTerrain terrain) => CornerMask(WfcTerrains.ToRule(terrain));

        public ulong AllEdges()
        {
            ulong mask = 0ul;
            for (int i = 0; i < EdgeConnections.Count && i < WfcConnections.Max; i++)
                if (EdgeConnections[i] != null) mask |= WfcConnections.Bit(i);
            return mask;
        }

        /// <summary>Edge connections units can walk across (bits by index).</summary>
        public ulong WalkableEdges()
        {
            ulong mask = 0ul;
            for (int i = 0; i < EdgeConnections.Count && i < WfcConnections.Max; i++)
                if (EdgeConnections[i] != null && EdgeConnections[i].Walkable) mask |= WfcConnections.Bit(i);
            return mask;
        }

        /// <summary>The terrain of each corner connection, by index.</summary>
        public byte[] CornerTerrains()
        {
            var terrains = new byte[CornerConnections.Count];
            for (int i = 0; i < terrains.Length; i++) terrains[i] = (byte)(CornerConnections[i]?.Terrain ?? WfcTerrain.NotWalkable);
            return terrains;
        }

        /// <summary>The ground height of each corner connection, by index.</summary>
        public float[] CornerHeights()
        {
            var heights = new float[CornerConnections.Count];
            for (int i = 0; i < heights.Length; i++) heights[i] = CornerConnections[i]?.Height ?? 0f;
            return heights;
        }

        /// <summary>1 for each edge connection units can walk across, by index.</summary>
        public byte[] EdgeWalkables()
        {
            var walkables = new byte[EdgeConnections.Count];
            for (int i = 0; i < walkables.Length; i++) walkables[i] = (byte)(EdgeConnections[i] == null || EdgeConnections[i].Walkable ? 1 : 0);
            return walkables;
        }

        /// <summary>4 corner connection indices per tile (SW, SE, NE, NW), LevelSurface.Unknown where a tile or connection is missing.</summary>
        public byte[] TileCornerIndices()
        {
            var indices = new byte[Tiles.Count * 4];
            for (int t = 0; t < Tiles.Count; t++)
                for (int k = 0; k < 4; k++)
                {
                    int index = Tiles[t] != null ? CornerIndex(Tiles[t].Corners[k]) : -1;
                    indices[t * 4 + k] = index >= 0 && index < LevelSurface.Unknown ? (byte)index : LevelSurface.Unknown;
                }
            return indices;
        }

        /// <summary>4 edge connection indices per tile (S, E, N, W), LevelSurface.Unknown where a tile or connection is missing.</summary>
        public byte[] TileEdgeIndices()
        {
            var indices = new byte[Tiles.Count * 4];
            for (int t = 0; t < Tiles.Count; t++)
                for (int k = 0; k < 4; k++)
                {
                    int index = Tiles[t] != null ? EdgeIndex(Tiles[t].Edges[k]) : -1;
                    indices[t * 4 + k] = index >= 0 && index < LevelSurface.Unknown ? (byte)index : LevelSurface.Unknown;
                }
            return indices;
        }

        /// <summary>How high a model with these corners is placed: its lowest corner's height (0 when none is known).</summary>
        public float Raise(IEnumerable<int> cornerIds)
        {
            float lowest = float.MaxValue;
            foreach (int id in cornerIds)
            {
                var connection = CornerConnection(id);
                if (connection != null) lowest = Mathf.Min(lowest, connection.Height);
            }
            return lowest == float.MaxValue ? 0f : lowest;
        }

        public float[] TileRaises()
        {
            var raises = new float[Tiles.Count];
            for (int t = 0; t < raises.Length; t++) raises[t] = Tiles[t] != null ? Raise(Tiles[t].Corners) : 0f;
            return raises;
        }

        public float[] PieceRaises()
        {
            var raises = new float[Pieces.Count];
            for (int p = 0; p < raises.Length; p++) raises[p] = Pieces[p] != null ? Raise(Pieces[p].CornerIds) : 0f;
            return raises;
        }

        /// <summary>
        /// A copy of the tile with every corner swapped for its connection's Higher, so the same model fits one step up (it's
        /// raised by the new lowest corner). Edges stay. Null, with the reason, when a corner's connection has no Higher.
        /// </summary>
        public WfcTile CopyOneStepUp(WfcTile tile, out string problem)
        {
            problem = null;
            var corners = tile.Corners;
            for (int k = 0; k < 4; k++)
            {
                var connection = CornerConnection(corners[k]);
                if (connection == null || CornerConnection(connection.Higher) == null)
                {
                    problem = $"{connection?.Name ?? $"Id {corners[k]}"} has no Higher connection.";
                    return null;
                }
                corners[k] = connection.Higher;
            }
            return new WfcTile
            {
                Name = tile.Name + " High", Prefab = tile.Prefab, Weight = tile.Weight, AllowRotations = tile.AllowRotations, Background = tile.Background,
                AllowMirror = tile.AllowMirror, Looks = new List<GameObject>(tile.Looks ?? new List<GameObject>()),
                SouthWest = corners[0], SouthEast = corners[1], NorthEast = corners[2], NorthWest = corners[3],
                South = tile.South, East = tile.East, North = tile.North, West = tile.West,
            };
        }

        /// <summary>
        /// Every tile in every allowed rotation, without rotations that look the same to the solver (symmetric tiles).
        /// These are what the solver chooses between. Tiles naming a connection the set doesn't have are left out, and so are
        /// background tiles for the level (background: false) or the others for the background ring (background: true).
        /// </summary>
        public List<WfcVariant> BuildVariants(List<string> warnings = null, bool background = false)
        {
            if (CornerConnections.Count > WfcConnections.Max || EdgeConnections.Count > WfcConnections.Max)
                warnings?.Add($"A tile set can have at most {WfcConnections.Max} corner and {WfcConnections.Max} edge connections; tiles using the rest are left out.");

            var variants = new List<WfcVariant>();
            var cornerIndices = new int[4];
            var edgeIndices = new int[4];
            var corners = new int[4];
            var edges = new int[4];
            for (int t = 0; t < Tiles.Count; t++)
            {
                var tile = Tiles[t];
                if (tile == null || tile.Background != background) continue;
                bool known = true;
                for (int k = 0; k < 4; k++)
                {
                    cornerIndices[k] = CornerIndex(tile.Corners[k]);
                    edgeIndices[k] = EdgeIndex(tile.Edges[k]);
                    known &= cornerIndices[k] >= 0 && cornerIndices[k] < WfcConnections.Max && edgeIndices[k] >= 0 && edgeIndices[k] < WfcConnections.Max;
                }
                if (!known)
                {
                    warnings?.Add($"Tile {tile.Name} uses a connection the tile set doesn't have; it's left out.");
                    continue;
                }

                // Rotations that give the same connections are left out. A mirrored copy that fits like an unmirrored one
                // stays (it looks different), and the two share the tile's weight.
                int first = variants.Count;
                var keys = new List<long>();
                for (int m = 0; m < (tile.AllowMirror ? 2 : 1); m++)
                {
                    var seen = new HashSet<long>();
                    for (int r = 0; r < (tile.AllowRotations ? 4 : 1); r++)
                    {
                        WfcRotation.Turn(cornerIndices, r, m == 1, false, corners);
                        WfcRotation.Turn(edgeIndices, r, m == 1, true, edges);
                        long key = 0;
                        for (int k = 0; k < 4; k++) key = (key << 12) | (long)(corners[k] << 6 | edges[k]);
                        if (!seen.Add(key)) continue;
                        keys.Add(key);
                        variants.Add(new WfcVariant
                        {
                            Tile = t,
                            Rotation = r,
                            Mirror = m == 1,
                            Corners = new int4(corners[0], corners[1], corners[2], corners[3]),
                            Edges = new int4(edges[0], edges[1], edges[2], edges[3]),
                        });
                    }
                }
                for (int i = first; i < variants.Count; i++)
                {
                    var variant = variants[i];
                    variant.Weight = Mathf.Max(0f, tile.Weight) / keys.FindAll(k => k == keys[i - first]).Count;
                    variants[i] = variant;
                }
            }
            return variants;
        }
    }

    [Serializable]
    public class WfcTile
    {
        public string Name;
        public GameObject Prefab;
        [Tooltip("Corner connection Ids")]
        public int SouthWest, SouthEast, NorthEast, NorthWest;
        [Tooltip("Edge connection Ids")]
        public int South, East, North, West;
        [Tooltip("How often the solver picks this tile compared to others that fit")]
        [Min(0f)] public float Weight = 1f;
        [Tooltip("Also use the tile turned 90°, 180° and 270°")]
        public bool AllowRotations = true;
        [Tooltip("Also use the tile mirrored (east and west swapped). Leave it off for tiles with one-sided edges, " +
                 "like a ramp's west and east slope edges.")]
        public bool AllowMirror;
        [Tooltip("Only used in the background ring around the level (scenery, no colliders), never inside it")]
        public bool Background;
        [Tooltip("Other models for the same tile. Each cell picks one (or Prefab) at random, so neighboring cells look different.")]
        public List<GameObject> Looks = new List<GameObject>();

        /// <summary>Prefab, then Looks: look 0 is Prefab.</summary>
        public int LookCount => 1 + (Looks?.Count ?? 0);

        public GameObject Look(int look) => look <= 0 || Looks == null || look > Looks.Count ? Prefab : Looks[look - 1];

        /// <summary>Corner connection Ids: SouthWest, SouthEast, NorthEast, NorthWest.</summary>
        public int[] Corners => new[] { SouthWest, SouthEast, NorthEast, NorthWest };

        /// <summary>Edge connection Ids: South, East, North, West.</summary>
        public int[] Edges => new[] { South, East, North, West };
    }

    [Serializable]
    public class WfcPiece
    {
        public string Name;
        public GameObject Prefab;
        [Tooltip("Footprint in cells")]
        public Vector2Int Size = new Vector2Int(2, 2);
        [Tooltip("(Size.x + 1) * (Size.y + 1) corner connection Ids, row by row starting at the south-west corner. The edges along the outline fit any tile.")]
        public List<int> CornerIds = new List<int>();
        public bool AllowRotations = true;

        // Corner terrains from before connections (the terrain value is the connection Id)
        [SerializeField, HideInInspector, FormerlySerializedAs("Corners")] List<WfcTerrain> _terrainCorners = new List<WfcTerrain>();

        public int CornerCount => (Mathf.Max(1, Size.x) + 1) * (Mathf.Max(1, Size.y) + 1);

        /// <summary>Resizes CornerIds to match Size (new corners get connection Id 0).</summary>
        public void EnsureCorners()
        {
            MigrateCorners();
            while (CornerIds.Count < CornerCount) CornerIds.Add(0);
            if (CornerIds.Count > CornerCount) CornerIds.RemoveRange(CornerCount, CornerIds.Count - CornerCount);
        }

        public void MigrateCorners()
        {
            if (_terrainCorners == null || _terrainCorners.Count == 0) return;
            if (CornerIds.Count == 0)
                foreach (var terrain in _terrainCorners) CornerIds.Add((int)terrain);
            _terrainCorners.Clear();
        }
    }

    /// <summary>A tile in one rotation (maybe mirrored first), as the solver sees it: connection indices (bits), not Ids.</summary>
    public struct WfcVariant
    {
        public int Tile;
        public int Rotation;
        public bool Mirror;
        /// <summary>Corner connection indices: SouthWest, SouthEast, NorthEast, NorthWest.</summary>
        public int4 Corners;
        /// <summary>Edge connection indices: South, East, North, West.</summary>
        public int4 Edges;
        public float Weight;
    }

    /// <summary>90° rotations around Y (Quaternion.Euler(0, 90 * r, 0)): east turns to south.</summary>
    public static class WfcRotation
    {
        /// <summary>
        /// Corners (SW, SE, NE, NW) or edges (S, E, N, W) of a tile turned r times: the one at j comes from (j + r) % 4.
        /// </summary>
        public static void RotateCorners<T>(IReadOnlyList<T> corners, int rotation, T[] result)
        {
            for (int j = 0; j < 4; j++) result[j] = corners[(j + rotation) % 4];
        }

        /// <summary>
        /// Where corner (or edge) j of a tile mirrored (east and west swapped, x → -x) and then turned r times comes from in
        /// the tile: mirroring swaps SW with SE and NE with NW, or the east edge with the west one.
        /// </summary>
        public static int Source(int j, int rotation, bool mirror, bool edges)
        {
            int turned = (j + rotation) % 4;
            if (!mirror) return turned;
            return edges ? (4 - turned) % 4 : turned ^ 1;
        }

        /// <summary>Corners (edges: true for S, E, N, W) of a tile mirrored (when asked) and turned r times.</summary>
        public static void Turn<T>(IReadOnlyList<T> values, int rotation, bool mirror, bool edges, T[] result)
        {
            for (int j = 0; j < 4; j++) result[j] = values[Source(j, rotation, mirror, edges)];
        }

        /// <summary>Unique number for 4 corner terrains (SW + 3 SE + 9 NE + 27 NW). Used by the tile generators.</summary>
        public static int Code(IReadOnlyList<WfcTerrain> corners)
        {
            int code = 0, factor = 1;
            for (int i = 0; i < 4; i++, factor *= WfcTerrains.Count) code += (int)corners[i] * factor;
            return code;
        }

        public static void Decode(int code, WfcTerrain[] corners)
        {
            for (int i = 0; i < 4; i++, code /= WfcTerrains.Count) corners[i] = (WfcTerrain)(code % WfcTerrains.Count);
        }

        /// <summary>
        /// A piece's corner grid ((w+1) x (h+1), row by row from the south-west) turned r times. The footprint of a turned
        /// piece is (h x w) for odd r.
        /// </summary>
        public static T[] RotateGrid<T>(IReadOnlyList<T> corners, int width, int height, int rotation, out int rotatedWidth, out int rotatedHeight)
        {
            var current = new T[corners.Count];
            for (int i = 0; i < corners.Count; i++) current[i] = corners[i];
            int w = width, h = height;
            for (int r = 0; r < (rotation % 4 + 4) % 4; r++)
            {
                // One turn: corner (x, y) moves to (y, w - x) and the footprint becomes h x w
                var next = new T[current.Length];
                for (int y = 0; y <= h; y++)
                    for (int x = 0; x <= w; x++)
                        next[y + (w - x) * (h + 1)] = current[x + y * (w + 1)];
                current = next;
                (w, h) = (h, w);
            }
            rotatedWidth = w;
            rotatedHeight = h;
            return current;
        }

        public static Quaternion ToQuaternion(int rotation) => Quaternion.Euler(0f, 90f * rotation, 0f);
    }
}
