using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;

namespace LevelGeneration
{
    /// <summary>
    /// Shows the level's ambient VFX graph (LevelAmbientGraph): one effect over the whole level, so every player sees
    /// the same. It gets the level's bounds, the forest mask (where the trees are), the ground height and two point
    /// lists from the solved terrain: water (where mist drifts, with the way the water flows) and splashes (river water
    /// beside a bridge or rocks). For the eyes only; built from the solved level, so every machine sees the same.
    /// </summary>
    public static class LevelAmbientGraphView
    {
        static GameObject _view;
        static Texture2D _ground;
        static GraphicsBuffer _water, _splashes;

        static readonly int BoundsCenterId = Shader.PropertyToID("BoundsCenter"), BoundsSizeId = Shader.PropertyToID("BoundsSize"),
                            ForestId = Shader.PropertyToID("Forest"), ForestMatrixId = Shader.PropertyToID("ForestMatrix"),
                            GroundId = Shader.PropertyToID("Ground"), GroundMatrixId = Shader.PropertyToID("GroundMatrix"),
                            WaterId = Shader.PropertyToID("Water"), WaterCountId = Shader.PropertyToID("WaterCount"),
                            SplashesId = Shader.PropertyToID("Splashes"), SplashCountId = Shader.PropertyToID("SplashCount");

        /// <param name="ground">The level's corners at ground height, in grid space (LevelStream.Ground)</param>
        /// <param name="flow">Which way the water flows at each corner of the bigger grid (LevelFlow, with the ring margin)</param>
        public static void Build(VisualEffectAsset graph, ref LevelLayoutBlob blob, float3[] ground, byte[] terrain, float2[] flow, int margin,
                                 Texture forestMask, Matrix4x4 forestMatrix)
        {
            Clear();
            int cw = blob.Width + 1, ch = blob.Height + 1;
            var toGrid = Matrix4x4.TRS(blob.Origin, blob.Rotation, Vector3.one).inverse;

            // Ground height (world y) per corner, sampled by the graph between corners
            _ground = new Texture2D(cw, ch, TextureFormat.RFloat, false, true)
                { name = "PL_AmbientGround", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var heights = new float[cw * ch];
            for (int i = 0; i < heights.Length; i++) heights[i] = blob.Origin.y + ground[i].y;
            _ground.SetPixelData(heights, 0);
            _ground.Apply(false);
            var groundMatrix = Matrix4x4.Translate(new Vector3(0.5f / cw, 0f, 0.5f / ch)) *
                               Matrix4x4.Scale(new Vector3(1f / (blob.TileSize * cw), 1f, 1f / (blob.TileSize * ch))) * toGrid;

            // Water corners with water on two sides or more (mist), and river corners beside a crossing or rocks (splashes):
            // two float4 each, the world position and the world flow (xz)
            bool Is(int x, int y, WfcTerrain kind) => x >= 0 && y >= 0 && x < cw && y < ch && terrain[x + y * cw] == (byte)kind;
            bool Crossing(int x, int y) => !Is(x, y, WfcTerrain.Water) && ((Is(x - 1, y, WfcTerrain.Water) && Is(x + 1, y, WfcTerrain.Water)) ||
                                                                          (Is(x, y - 1, WfcTerrain.Water) && Is(x, y + 1, WfcTerrain.Water)));
            var water = new List<Vector4>();
            var splashes = new List<Vector4>();
            var sides = new[] { new int2(1, 0), new int2(-1, 0), new int2(0, 1), new int2(0, -1) };
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < cw; x++)
                {
                    if (!Is(x, y, WfcTerrain.Water)) continue;
                    int wet = 0;
                    bool beside = false;
                    foreach (var s in sides)
                    {
                        if (Is(x + s.x, y + s.y, WfcTerrain.Water)) wet++;
                        beside |= Crossing(x + s.x, y + s.y) || Is(x + s.x, y + s.y, WfcTerrain.NotWalkable);
                    }
                    var position = blob.Origin + math.mul(blob.Rotation, ground[x + y * cw]);
                    var f = flow[(x + margin) + (y + margin) * (cw + 2 * margin)];
                    var worldFlow = math.mul(blob.Rotation, new float3(f.x, 0f, f.y));
                    var pair = (new Vector4(position.x, position.y, position.z, 0f), new Vector4(worldFlow.x, 0f, worldFlow.z, 0f));
                    if (wet >= 2) { water.Add(pair.Item1); water.Add(pair.Item2); }
                    if (beside && math.lengthsq(f) > 0.25f) { splashes.Add(pair.Item1); splashes.Add(pair.Item2); }
                }
            _water = Buffer(water);
            _splashes = Buffer(splashes);

            _view = new GameObject("Level Ambient");
            var effect = _view.AddComponent<VisualEffect>();
            effect.visualEffectAsset = graph;
            var size = new Vector3(blob.Width * blob.TileSize, 0f, blob.Height * blob.TileSize);
            var center = blob.Origin + math.mul(blob.Rotation, new float3(size.x * 0.5f, 0f, size.z * 0.5f));
            effect.SetVector3(BoundsCenterId, (Vector3)center + Vector3.up * 4f);
            effect.SetVector3(BoundsSizeId, new Vector3(Mathf.Max(size.x, size.z) * 1.5f, 14f, Mathf.Max(size.x, size.z) * 1.5f));
            effect.SetTexture(ForestId, forestMask);
            effect.SetMatrix4x4(ForestMatrixId, forestMatrix);
            effect.SetTexture(GroundId, _ground);
            effect.SetMatrix4x4(GroundMatrixId, groundMatrix);
            effect.SetGraphicsBuffer(WaterId, _water);
            effect.SetUInt(WaterCountId, (uint)(water.Count / 2));
            effect.SetGraphicsBuffer(SplashesId, _splashes);
            effect.SetUInt(SplashCountId, (uint)(splashes.Count / 2));
            effect.Reinit();
        }

        public static void Clear()
        {
            if (_view != null) Object.Destroy(_view);
            if (_ground != null) Object.Destroy(_ground);
            _water?.Release();
            _splashes?.Release();
            _view = null;
            _ground = null;
            _water = _splashes = null;
        }

        // Never empty (a graph can't take an empty buffer): an unused point when there's none
        static GraphicsBuffer Buffer(List<Vector4> values)
        {
            if (values.Count == 0) { values.Add(Vector4.zero); values.Add(Vector4.zero); }
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, values.Count, 16);
            buffer.SetData(values);
            return buffer;
        }
    }
}
