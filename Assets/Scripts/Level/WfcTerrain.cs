using UnityEngine;

namespace LevelGeneration
{
    /// <summary>
    /// What a tile corner is. Tiles are described by their 4 corner terrains, so two tiles fit side by side when their
    /// shared corners match. Values are stored in assets: keep them and add new ones at the end.
    /// </summary>
    public enum WfcTerrain : byte
    {
        Walkable = 0,
        Wall = 1,
        Water = 2,
    }

    /// <summary>A painted corner rule: Any, or a forced terrain (terrain + 1).</summary>
    public enum WfcCornerRule : byte
    {
        Any = 0,
        Walkable = 1,
        Wall = 2,
        Water = 3,
    }

    public static class WfcTerrains
    {
        public const int Count = 3;

        /// <summary>Bit mask with every terrain allowed.</summary>
        public const byte AllMask = (1 << Count) - 1;

        public static byte Mask(WfcTerrain terrain) => (byte)(1 << (int)terrain);

        /// <summary>The terrains a corner rule allows, as a bit mask.</summary>
        public static byte Mask(WfcCornerRule rule) => rule == WfcCornerRule.Any ? AllMask : Mask((WfcTerrain)((int)rule - 1));

        public static WfcCornerRule ToRule(WfcTerrain terrain) => (WfcCornerRule)((int)terrain + 1);

        public static Color Color(WfcTerrain terrain) => terrain switch
        {
            WfcTerrain.Walkable => new Color(0.45f, 0.75f, 0.35f),
            WfcTerrain.Wall => new Color(0.42f, 0.42f, 0.48f),
            WfcTerrain.Water => new Color(0.2f, 0.5f, 0.95f),
            _ => UnityEngine.Color.magenta,
        };

        /// <summary>One letter per terrain for names: F(loor), W(all), L(ake).</summary>
        public static char Letter(WfcTerrain terrain) => terrain switch
        {
            WfcTerrain.Walkable => 'F',
            WfcTerrain.Wall => 'W',
            WfcTerrain.Water => 'L',
            _ => '?',
        };
    }
}
