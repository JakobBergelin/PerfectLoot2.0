using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace LevelGeneration
{
    /// <summary>
    /// Builds the level's geometry: every placed tile's and piece's meshes and colliders are bent onto the deformed grid
    /// and merged per chunk of cells (meshes per material, colliders per layer). Props are collected with their bent
    /// position for the caller to spawn. Everything is in grid space; the created entities carry the layout's origin
    /// and turn.
    /// </summary>
    public class LevelMeshBuilder
    {
        const int ChunkSize = 8;

        public struct Prop
        {
            public int Prefab;
            public float3 Position;     // Grid space
            public quaternion Rotation; // Grid space
        }

        class MeshBatch
        {
            public int Material;
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Indices = new List<int>();
        }

        class ColliderBatch
        {
            public int Layer;
            public uint CollidesWith;
            public bool Ground;
            public readonly List<float3> Vertices = new List<float3>();
            public readonly List<int3> Triangles = new List<int3>();
        }

        readonly float3[] _corners;
        readonly int _width, _height;
        readonly float _tileSize;
        readonly Dictionary<(int2 Chunk, int Material), MeshBatch> _meshes = new Dictionary<(int2, int), MeshBatch>();
        readonly Dictionary<(int2 Chunk, int Layer, uint CollidesWith, bool Ground), ColliderBatch> _colliders =
            new Dictionary<(int2, int, uint, bool), ColliderBatch>();

        public readonly List<Prop> Props = new List<Prop>();

        public LevelMeshBuilder(float3[] bentCorners, int width, int height, float tileSize)
        {
            _corners = bentCorners;
            _width = width;
            _height = height;
            _tileSize = tileSize;
        }

        /// <summary>
        /// Adds a tile or piece: center is its footprint's middle in cells, rotation its 90° steps, cell any cell it covers
        /// (picks the chunk).
        /// </summary>
        public void Add(ref LevelPartGeometry part, float2 center, int rotation, int2 cell)
        {
            var turn = quaternion.RotateY(math.radians(90f * rotation));
            var chunk = cell / ChunkSize;

            for (int m = 0; m < part.Meshes.Length; m++)
            {
                ref var mesh = ref part.Meshes[m];
                if (!_meshes.TryGetValue((chunk, mesh.Material), out var batch))
                    _meshes[(chunk, mesh.Material)] = batch = new MeshBatch { Material = mesh.Material };
                int start = batch.Vertices.Count;
                for (int v = 0; v < mesh.Vertices.Length; v++)
                {
                    batch.Vertices.Add(Bend(turn, center, mesh.Vertices[v]));
                    batch.Uvs.Add(mesh.Uvs[v]);
                }
                for (int i = 0; i < mesh.Indices.Length; i++) batch.Indices.Add(start + mesh.Indices[i]);
            }

            for (int c = 0; c < part.Colliders.Length; c++)
            {
                ref var collider = ref part.Colliders[c];
                var key = (chunk, collider.Layer, collider.CollidesWith, collider.Ground);
                if (!_colliders.TryGetValue(key, out var batch))
                    _colliders[key] = batch = new ColliderBatch { Layer = collider.Layer, CollidesWith = collider.CollidesWith, Ground = collider.Ground };
                int start = batch.Vertices.Count;
                for (int v = 0; v < collider.Vertices.Length; v++) batch.Vertices.Add(Bend(turn, center, collider.Vertices[v]));
                for (int i = 0; i + 2 < collider.Indices.Length; i += 3)
                    batch.Triangles.Add(new int3(start + collider.Indices[i], start + collider.Indices[i + 1], start + collider.Indices[i + 2]));
            }

            for (int p = 0; p < part.Props.Length; p++)
            {
                var prop = part.Props[p];
                Props.Add(new Prop
                {
                    Prefab = prop.Prefab,
                    Position = Bend(turn, center, prop.Position),
                    Rotation = math.mul(turn, prop.Rotation),
                });
            }
        }

        // A point in the part's own space, turned with the part, placed on the bent grid; its height stays on top
        float3 Bend(quaternion turn, float2 center, float3 local)
        {
            var turned = math.mul(turn, local);
            var ground = LevelDeformation.Sample(_corners, _width, _height, center.x + turned.x / _tileSize, center.y + turned.z / _tileSize);
            return ground + new float3(0f, turned.y, 0f);
        }

        /// <summary>
        /// Creates the render and collider entities at the layout's origin and turn. Created meshes and colliders are
        /// added to the lists so the caller can free them. Returns how many render and collider entities were made.
        /// </summary>
        public (int Renderers, int Colliders) CreateEntities(EntityManager entityManager, float3 origin, quaternion rotation, Material[] materials,
                                                             List<Mesh> createdMeshes, List<BlobAssetReference<Unity.Physics.Collider>> createdColliders)
        {
            var transform = LocalTransform.FromPositionRotation(origin, rotation);
            var localToWorld = new LocalToWorld { Value = float4x4.TRS(origin, rotation, 1f) };

            // Render chunks: one mesh per chunk and material, all in one RenderMeshArray
            var batches = new List<MeshBatch>(_meshes.Values);
            var meshes = new Mesh[batches.Count];
            for (int i = 0; i < batches.Count; i++)
            {
                var batch = batches[i];
                var mesh = new Mesh { name = "Level chunk", indexFormat = batch.Vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(batch.Vertices);
                mesh.SetUVs(0, batch.Uvs);
                mesh.SetTriangles(batch.Indices, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                meshes[i] = mesh;
                createdMeshes.Add(mesh);
            }
            if (meshes.Length > 0)
            {
                var renderMeshArray = new RenderMeshArray(materials, meshes);
                var description = new RenderMeshDescription(ShadowCastingMode.On, receiveShadows: true);
                for (int i = 0; i < batches.Count; i++)
                {
                    var entity = entityManager.CreateEntity();
                    entityManager.AddComponentData(entity, transform);
                    entityManager.AddComponentData(entity, localToWorld);
                    RenderMeshUtility.AddComponents(entity, entityManager, description, renderMeshArray,
                                                    MaterialMeshInfo.FromRenderMeshArrayIndices(batches[i].Material, i));
                }
            }

            // Static collider chunks: one mesh collider per chunk, layer and aimability
            int colliderCount = 0;
            foreach (var batch in _colliders.Values)
            {
                if (batch.Triangles.Count == 0) continue;
                using var vertices = new NativeArray<float3>(batch.Vertices.ToArray(), Allocator.Temp);
                using var triangles = new NativeArray<int3>(batch.Triangles.ToArray(), Allocator.Temp);
                var filter = new Unity.Physics.CollisionFilter { BelongsTo = 1u << batch.Layer, CollidesWith = batch.CollidesWith };
                var collider = Unity.Physics.MeshCollider.Create(vertices, triangles, filter);
                createdColliders.Add(collider);

                var entity = entityManager.CreateEntity();
                entityManager.AddComponentData(entity, transform);
                entityManager.AddComponentData(entity, localToWorld);
                entityManager.AddComponentData(entity, new Unity.Physics.PhysicsCollider { Value = collider });
                entityManager.AddSharedComponent(entity, new Unity.Physics.PhysicsWorldIndex());
                if (batch.Ground) entityManager.AddComponent<Ground>(entity);
                colliderCount++;
            }
            return (batches.Count, colliderCount);
        }
    }
}
