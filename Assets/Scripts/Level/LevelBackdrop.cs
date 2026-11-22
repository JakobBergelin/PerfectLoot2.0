using UnityEngine;

namespace LevelGeneration
{
    /// <summary>
    /// What lies beyond a level, per biome, so the world fades into the drop instead of stopping: the background ring
    /// sinks away from the level's edge, everything below the level fades into one abyss color (PL_Abyss.hlsl, set as
    /// global shader values while the level exists), mist layers float below it, and large silhouettes stand far out.
    /// Everything is built from the level's seed, so every player sees the same backdrop.
    /// </summary>
    [CreateAssetMenu(menuName = "Perfect Loot/Level/Backdrop", fileName = "Backdrop")]
    public class LevelBackdrop : ScriptableObject
    {
        [Header("Ring")]
        [Tooltip("Meters the background ring sinks at its outer edge, curving down from the level's edge")]
        public float RingSink = 15f;

        [Header("Abyss fade")]
        [Tooltip("What everything fades to deep down; the sky looks the same color downward")]
        public Color AbyssColor = new Color(0.07f, 0.24f, 0.27f);
        [Tooltip("Meters below the level's ground where the fade starts")]
        public float FadeStart = 1f;
        [Tooltip("Meters over which it reaches the full abyss color")]
        [Min(0.1f)] public float FadeDepth = 30f;
        [Tooltip("Hard steps in the fade (0 = smooth)")]
        [Min(0)] public int FadeBands = 5;

        [Header("Mist")]
        [Tooltip("Spawned once at the level's center, below the ground (its own children set their depths)")]
        public GameObject Mist;

        [Header("Silhouettes")]
        [Tooltip("Large scenery far outside the level (no colliders)")]
        public GameObject[] Pieces = new GameObject[0];
        [Min(0)] public int PieceCount = 30;
        [Tooltip("Meters outside the background ring, nearest to farthest")]
        public Vector2 PieceDistance = new Vector2(10f, 80f);
        [Tooltip("Meters below the level's ground for a piece's pivot, shallowest to deepest (pl_backdrop.py puts it at the " +
                 "top, so no piece rises above the level)")]
        public Vector2 PieceDepth = new Vector2(5f, 40f);
        public Vector2 PieceScale = new Vector2(0.8f, 1.6f);
        [Tooltip("Pieces keep at least this many meters apart")]
        [Min(0f)] public float PieceSpacing = 14f;
    }
}
