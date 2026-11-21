using UnityEngine;

namespace LevelGeneration
{
    /// <summary>
    /// How a tile corner plays: where units can walk, where they can't (walls, cliffs, chasms, ...) and water. Which tiles
    /// fit together is decided by connections (WfcCornerConnection), each of which has one of these. Values are stored in
    /// assets: keep them and add new ones at the end.
    /// </summary>
    public enum WfcTerrain : byte
    {
        Walkable = 0,
        NotWalkable = 1,
        Water = 2,
    }

    /// <summary>A painted corner rule's terrain: Any, or a forced terrain (terrain + 1).</summary>
    public enum WfcCornerRule : byte
    {
        Any = 0,
        Walkable = 1,
        NotWalkable = 2,
        Water = 3,
    }

    public static class WfcTerrains
    {
        public const int Count = 3;

        /// <summary>True when a corner connection with this terrain passes the rule.</summary>
        public static bool Allows(WfcCornerRule rule, WfcTerrain terrain) => rule == WfcCornerRule.Any || (int)rule - 1 == (int)terrain;

        public static WfcCornerRule ToRule(WfcTerrain terrain) => (WfcCornerRule)((int)terrain + 1);

        public static Color Color(WfcTerrain terrain) => terrain switch
        {
            WfcTerrain.Walkable => new Color(0.45f, 0.75f, 0.35f),
            WfcTerrain.NotWalkable => new Color(0.42f, 0.42f, 0.48f),
            WfcTerrain.Water => new Color(0.2f, 0.5f, 0.95f),
            _ => UnityEngine.Color.magenta,
        };

        /// <summary>One letter per terrain for names: F(loor), W(all), L(ake).</summary>
        public static char Letter(WfcTerrain terrain) => terrain switch
        {
            WfcTerrain.Walkable => 'F',
            WfcTerrain.NotWalkable => 'W',
            WfcTerrain.Water => 'L',
            _ => '?',
        };
    }
}
