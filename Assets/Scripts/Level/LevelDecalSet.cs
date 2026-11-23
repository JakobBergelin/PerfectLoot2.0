using System;
using System.Collections.Generic;
using UnityEngine;

namespace LevelGeneration
{
    /// <summary>
    /// The marks a biome paints on its floors (LevelDecalsView): moss and fallen leaves along the forest's edge, mud by the
    /// water, flower patches in the clearings. Decal materials (URP's decal shader); placed from the level's seed on the
    /// solved terrain. Only floor materials take them. For the eyes only.
    /// </summary>
    [CreateAssetMenu(fileName = "LevelDecalSet", menuName = "Level/Decal Set")]
    public class LevelDecalSet : ScriptableObject
    {
        public enum Spot : byte
        {
            ForestEdge,     // Walkable corners beside not-walkable ground (trees, rocks)
            Shore,          // Walkable corners beside water
            Open,           // Walkable corners with walkable ground all around
        }

        [Serializable]
        public class Decal
        {
            public Material Material;
            public Spot Spot;
            [Min(0)] public int Count = 20;
            [Tooltip("Meters across, min and max")]
            public Vector2 Size = new Vector2(1.5f, 3f);
        }

        public List<Decal> Decals = new List<Decal>();
    }
}
