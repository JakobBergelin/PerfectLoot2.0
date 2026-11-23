using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace LevelGeneration
{
    /// <summary>
    /// Paints the level's floor decals (LevelDecalSet, the layout's LevelDecal buffer): each kind on random corners of its
    /// spot, picked with the level's seed so every machine paints the same. URP decal projectors pointing down; they stay
    /// with the level. For the eyes only.
    /// </summary>
    public static class LevelDecalsView
    {
        const float Reach = 3f;     // Meters a projector reaches down from just above the ground

        static GameObject _root;

        /// <param name="ground">The level's corners at ground height, in grid space (LevelStream.Ground)</param>
        public static void Build(NativeArray<LevelDecal> decals, ref LevelLayoutBlob blob, float3[] ground, byte[] terrain, uint seed)
        {
            Clear();
            int cw = blob.Width + 1, ch = blob.Height + 1;
            byte At(int x, int y) => x < 0 || y < 0 || x >= cw || y >= ch ? (byte)WfcTerrain.NotWalkable : terrain[x + y * cw];
            var spots = new[] { new List<int2>(), new List<int2>(), new List<int2>() };
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < cw; x++)
                {
                    if (At(x, y) != (byte)WfcTerrain.Walkable) continue;
                    int walls = 0, wet = 0, walkable = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            byte other = At(x + dx, y + dy);
                            bool side = dx == 0 || dy == 0;
                            if (side && other == (byte)WfcTerrain.NotWalkable) walls++;
                            if (side && other == (byte)WfcTerrain.Water) wet++;
                            if (other == (byte)WfcTerrain.Walkable) walkable++;
                        }
                    if (walls > 0) spots[(int)LevelDecalSet.Spot.ForestEdge].Add(new int2(x, y));
                    if (wet > 0) spots[(int)LevelDecalSet.Spot.Shore].Add(new int2(x, y));
                    if (walkable == 8) spots[(int)LevelDecalSet.Spot.Open].Add(new int2(x, y));
                }

            _root = new GameObject("Level Decals");
            var random = new Unity.Mathematics.Random(math.max(1u, math.hash(new uint2(seed, 0xDECAu))));
            foreach (var decal in decals)
            {
                var material = decal.Material.Value;
                var list = spots[(int)decal.Spot];
                if (material == null || list.Count == 0) continue;
                for (int i = 0; i < decal.Count; i++)
                {
                    // A corner of its spot, nudged up to half a cell
                    var corner = list[random.NextInt(list.Count)];
                    var local = ground[corner.x + corner.y * cw] + new float3(random.NextFloat(-0.5f, 0.5f), 0f, random.NextFloat(-0.5f, 0.5f)) * blob.TileSize;
                    var position = blob.Origin + math.mul(blob.Rotation, local);
                    float size = random.NextFloat(decal.Size.x, decal.Size.y);
                    var go = new GameObject("Decal");
                    go.transform.SetParent(_root.transform, false);
                    go.transform.SetPositionAndRotation((Vector3)position + Vector3.up * 0.6f, Quaternion.Euler(90f, random.NextFloat(360f), 0f));
                    var projector = go.AddComponent<DecalProjector>();
                    projector.material = material;
                    projector.size = new Vector3(size, size, Reach);
                    projector.pivot = new Vector3(0f, 0f, Reach * 0.5f);
                    projector.startAngleFade = 50f;
                    projector.endAngleFade = 70f;
                }
            }
        }

        public static void Clear()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
        }
    }
}
