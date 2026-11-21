using System;
using UnityEngine;

namespace LevelGeneration
{
    /// <summary>
    /// What a tile corner can be. Two tiles fit side by side when their shared corners have the same connection, so a
    /// tile set can have as many kinds of ground as it likes; several can share one terrain (grass, cobble and moss are
    /// all Walkable). Tiles store the Id, so reordering or renaming a connection changes no tile.
    /// </summary>
    [Serializable]
    public class WfcCornerConnection
    {
        public int Id;
        public string Name;
        public Color Color = Color.gray;
        [Tooltip("How the corner plays: walkable, not walkable or water")]
        public WfcTerrain Terrain;
        [Tooltip("Meters above the grid that the ground is at this corner. A tile's model is raised to its lowest corner's height, " +
                 "so a ramp or cliff model starts at its low side and a high-ground copy starts at the high ground.")]
        public float Height;
        [Tooltip("The same connection one step up (Id, or -1 for none), for the tile set's \"Copy one step up\" button")]
        public int Higher = -1;
    }

    /// <summary>What a tile edge can be. Two tiles fit side by side when their shared edge has the same connection.</summary>
    [Serializable]
    public class WfcEdgeConnection
    {
        public int Id;
        public string Name;
        public Color Color = Color.white;
        [Tooltip("Units can walk across this edge (plain ground or a ramp can be crossed, a cliff can't)")]
        public bool Walkable = true;
    }

    public static class WfcConnections
    {
        /// <summary>Connections per kind (corner or edge): the solver keeps one bit per connection in a 64-bit mask.</summary>
        public const int Max = 64;

        public static ulong Bit(int index) => 1ul << index;

        /// <summary>A mask with the first count connections.</summary>
        public static ulong All(int count) => count >= Max ? ~0ul : (1ul << count) - 1;
    }
}
