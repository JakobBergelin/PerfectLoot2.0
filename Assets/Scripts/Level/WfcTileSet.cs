using System;
using System.Collections.Generic;
using UnityEngine;

namespace LevelGeneration
{
    /// <summary>
    /// The tiles and multi-cell pieces a level is built from, each described by its corner terrains. Corners are
    /// ordered SouthWest, SouthEast, NorthEast, NorthWest (+X is east, +Z is north). Prefabs have their pivot in the
    /// middle of their footprint and are rotated around Y in 90° steps.
    /// </summary>
    [CreateAssetMenu(fileName = "TileSet", menuName = "Level/WFC Tile Set")]
    public class WfcTileSet : ScriptableObject
    {
        public List<WfcTile> Tiles = new List<WfcTile>();
        [Tooltip("Larger pieces (e.g. a 2x2 arena) placed by hand in the level layout")]
        public List<WfcPiece> Pieces = new List<WfcPiece>();

        /// <summary>
        /// Every tile in every allowed rotation, without rotations that give the same corners (symmetric tiles).
        /// These are what the solver chooses between.
        /// </summary>
        public List<WfcVariant> BuildVariants()
        {
            var variants = new List<WfcVariant>();
            var corners = new WfcTerrain[4];
            for (int t = 0; t < Tiles.Count; t++)
            {
                var tile = Tiles[t];
                if (tile == null) continue;
                var seen = new HashSet<int>();
                int rotations = tile.AllowRotations ? 4 : 1;
                for (int r = 0; r < rotations; r++)
                {
                    WfcRotation.RotateCorners(tile.Corners, r, corners);
                    if (!seen.Add(WfcRotation.Code(corners))) continue;
                    variants.Add(new WfcVariant
                    {
                        Tile = t,
                        Rotation = r,
                        SouthWest = corners[0], SouthEast = corners[1], NorthEast = corners[2], NorthWest = corners[3],
                        Weight = Mathf.Max(0f, tile.Weight),
                    });
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
        public WfcTerrain SouthWest, SouthEast, NorthEast, NorthWest;
        [Tooltip("How often the solver picks this tile compared to others that fit")]
        [Min(0f)] public float Weight = 1f;
        [Tooltip("Also use the tile turned 90°, 180° and 270°")]
        public bool AllowRotations = true;

        /// <summary>SouthWest, SouthEast, NorthEast, NorthWest.</summary>
        public WfcTerrain[] Corners => new[] { SouthWest, SouthEast, NorthEast, NorthWest };
    }

    [Serializable]
    public class WfcPiece
    {
        public string Name;
        public GameObject Prefab;
        [Tooltip("Footprint in cells")]
        public Vector2Int Size = new Vector2Int(2, 2);
        [Tooltip("(Size.x + 1) * (Size.y + 1) corners, row by row starting at the south-west corner")]
        public List<WfcTerrain> Corners = new List<WfcTerrain>();
        public bool AllowRotations = true;

        public int CornerCount => (Mathf.Max(1, Size.x) + 1) * (Mathf.Max(1, Size.y) + 1);

        /// <summary>Resizes Corners to match Size (new corners are Walkable).</summary>
        public void EnsureCorners()
        {
            while (Corners.Count < CornerCount) Corners.Add(WfcTerrain.Walkable);
            if (Corners.Count > CornerCount) Corners.RemoveRange(CornerCount, Corners.Count - CornerCount);
        }
    }

    /// <summary>A tile in one rotation, as the solver sees it.</summary>
    public struct WfcVariant
    {
        public int Tile;
        public int Rotation;
        public WfcTerrain SouthWest, SouthEast, NorthEast, NorthWest;
        public float Weight;

        public WfcTerrain Corner(int index) => index switch
        {
            0 => SouthWest,
            1 => SouthEast,
            2 => NorthEast,
            _ => NorthWest,
        };
    }

    /// <summary>90° rotations around Y (Quaternion.Euler(0, 90 * r, 0)): east turns to south.</summary>
    public static class WfcRotation
    {
        /// <summary>Corners (SW, SE, NE, NW) of a tile turned r times: the corner at j comes from (j + r) % 4.</summary>
        public static void RotateCorners(IReadOnlyList<WfcTerrain> corners, int rotation, WfcTerrain[] result)
        {
            for (int j = 0; j < 4; j++) result[j] = corners[(j + rotation) % 4];
        }

        /// <summary>Unique number for 4 corners (SW + 3 SE + 9 NE + 27 NW).</summary>
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
        public static WfcTerrain[] RotateGrid(IReadOnlyList<WfcTerrain> corners, int width, int height, int rotation, out int rotatedWidth, out int rotatedHeight)
        {
            var current = new WfcTerrain[corners.Count];
            for (int i = 0; i < corners.Count; i++) current[i] = corners[i];
            int w = width, h = height;
            for (int r = 0; r < (rotation % 4 + 4) % 4; r++)
            {
                // One turn: corner (x, y) moves to (y, w - x) and the footprint becomes h x w
                var next = new WfcTerrain[current.Length];
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
