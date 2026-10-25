using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace LevelGeneration
{
    /// <summary>
    /// Builds one chunk of the level with Burst jobs: every placed tile's and piece's meshes and colliders are bent onto
    /// the deformed grid and merged (meshes per material, colliders per layer). Schedule does the cheap bookkeeping on
    /// the main thread and starts the jobs: bending (parallel), outline normals per mesh, and a physics mesh collider per
    /// collider batch. Finish, once Handle is done, makes the meshes and entities. Positions are in grid space; the
    /// entities carry the layout's origin and turn. Props are listed with their bent pose for the caller to spawn.
    /// </summary>
    public class LevelChunkBuilder
    {
        /// <summary>Cells per chunk side: meshes and colliders are merged, and streamed in, per chunk.</summary>
        public const int ChunkSize = 8;

        /// <summary>A tile or piece in the chunk: Center is its footprint's middle in cells, Rotation its 90° steps.</summary>
        public struct Instance
        {
            public bool IsPiece;
            public int Part;
            public float2 Center;
            public int Rotation;
        }

        public struct Prop
        {
            public int Prefab;
            public float3 Position;     // Grid space
            public quaternion Rotation; // Grid space
        }

        struct MeshItem { public int Instance, MeshPart, VertexStart, IndexStart, BatchVertexStart; }
        struct ColliderItem { public int Instance, ColliderPart, VertexStart, TriangleStart, BatchVertexStart; }
        struct MeshBatch { public int Material, VertexStart, VertexCount, IndexStart, IndexCount; }
        struct ColliderBatch { public int Layer; public uint CollidesWith; public bool Ground; public int VertexStart, VertexCount, TriangleStart, TriangleCount; }

        public JobHandle Handle { get; private set; }
        public readonly List<Prop> Props = new List<Prop>();

        NativeArray<Instance> _instances;
        NativeArray<MeshItem> _meshItems;
        NativeArray<ColliderItem> _colliderItems;
        NativeArray<MeshBatch> _meshBatches;
        NativeArray<ColliderBatch> _colliderBatches;
        NativeArray<float3> _vertices, _normals, _outlineNormals, _colliderVertices;
        NativeArray<float4> _tangents;
        NativeArray<float2> _uvs;
        NativeArray<int> _indices;
        NativeArray<int3> _triangles;
        NativeArray<BlobAssetReference<Unity.Physics.Collider>> _builtColliders;

        static ref LevelPartGeometry Part(ref LevelGeometryBlob geometry, Instance instance)
        {
            if (instance.IsPiece) return ref geometry.Pieces[instance.Part];
            return ref geometry.Tiles[instance.Part];
        }

        /// <summary>Lays out the chunk's merged arrays and starts its jobs.</summary>
        public static LevelChunkBuilder Schedule(BlobAssetReference<LevelGeometryBlob> geometryBlob, NativeArray<float3> corners,
                                                 int width, int height, float tileSize, List<Instance> instances)
        {
            var chunk = new LevelChunkBuilder();
            ref var geometry = ref geometryBlob.Value;

            // Bookkeeping: which batch each submesh / collider goes to, and where in the merged arrays
            var meshBatchOf = new Dictionary<int, int>();
            var meshBatches = new List<MeshBatch>();
            var meshItems = new List<(MeshItem Item, int Batch)>();
            var colliderBatchOf = new Dictionary<(int, uint, bool), int>();
            var colliderBatches = new List<ColliderBatch>();
            var colliderItems = new List<(ColliderItem Item, int Batch)>();

            for (int i = 0; i < instances.Count; i++)
            {
                ref var part = ref Part(ref geometry, instances[i]);
                for (int m = 0; m < part.Meshes.Length; m++)
                {
                    ref var mesh = ref part.Meshes[m];
                    if (!meshBatchOf.TryGetValue(mesh.Material, out int b))
                    {
                        meshBatchOf[mesh.Material] = b = meshBatches.Count;
                        meshBatches.Add(new MeshBatch { Material = mesh.Material });
                    }
                    var batch = meshBatches[b];
                    meshItems.Add((new MeshItem { Instance = i, MeshPart = m, VertexStart = batch.VertexCount, IndexStart = batch.IndexCount }, b));
                    batch.VertexCount += mesh.Vertices.Length;
                    batch.IndexCount += mesh.Indices.Length;
                    meshBatches[b] = batch;
                }
                for (int c = 0; c < part.Colliders.Length; c++)
                {
                    ref var collider = ref part.Colliders[c];
                    var key = (collider.Layer, collider.CollidesWith, collider.Ground);
                    if (!colliderBatchOf.TryGetValue(key, out int b))
                    {
                        colliderBatchOf[key] = b = colliderBatches.Count;
                        colliderBatches.Add(new ColliderBatch { Layer = collider.Layer, CollidesWith = collider.CollidesWith, Ground = collider.Ground });
                    }
                    var batch = colliderBatches[b];
                    colliderItems.Add((new ColliderItem { Instance = i, ColliderPart = c, VertexStart = batch.VertexCount, TriangleStart = batch.TriangleCount }, b));
                    batch.VertexCount += collider.Vertices.Length;
                    batch.TriangleCount += collider.Indices.Length / 3;
                    colliderBatches[b] = batch;
                }
                for (int p = 0; p < part.Props.Length; p++)
                {
                    var prop = part.Props[p];
                    var turn = quaternion.RotateY(math.radians(90f * instances[i].Rotation));
                    chunk.Props.Add(new Prop
                    {
                        Prefab = prop.Prefab,
                        Position = Bend(corners, width, height, tileSize, turn, instances[i].Center, prop.Position),
                        Rotation = math.mul(turn, prop.Rotation),
                    });
                }
            }

            // Batches one after another in the merged arrays
            int vertexTotal = 0, indexTotal = 0;
            for (int b = 0; b < meshBatches.Count; b++)
            {
                var batch = meshBatches[b];
                batch.VertexStart = vertexTotal;
                batch.IndexStart = indexTotal;
                vertexTotal += batch.VertexCount;
                indexTotal += batch.IndexCount;
                meshBatches[b] = batch;
            }
            int colliderVertexTotal = 0, triangleTotal = 0;
            for (int b = 0; b < colliderBatches.Count; b++)
            {
                var batch = colliderBatches[b];
                batch.VertexStart = colliderVertexTotal;
                batch.TriangleStart = triangleTotal;
                colliderVertexTotal += batch.VertexCount;
                triangleTotal += batch.TriangleCount;
                colliderBatches[b] = batch;
            }

            var allocator = Allocator.Persistent;   // Streamed chunks can take longer than TempJob allows
            chunk._instances = new NativeArray<Instance>(instances.ToArray(), allocator);
            chunk._meshItems = new NativeArray<MeshItem>(meshItems.Count, allocator);
            for (int i = 0; i < meshItems.Count; i++)
            {
                var (item, b) = meshItems[i];
                item.BatchVertexStart = meshBatches[b].VertexStart;
                item.VertexStart += meshBatches[b].VertexStart;
                item.IndexStart += meshBatches[b].IndexStart;
                chunk._meshItems[i] = item;
            }
            chunk._colliderItems = new NativeArray<ColliderItem>(colliderItems.Count, allocator);
            for (int i = 0; i < colliderItems.Count; i++)
            {
                var (item, b) = colliderItems[i];
                item.BatchVertexStart = colliderBatches[b].VertexStart;
                item.VertexStart += colliderBatches[b].VertexStart;
                item.TriangleStart += colliderBatches[b].TriangleStart;
                chunk._colliderItems[i] = item;
            }
            chunk._meshBatches = new NativeArray<MeshBatch>(meshBatches.ToArray(), allocator);
            chunk._colliderBatches = new NativeArray<ColliderBatch>(colliderBatches.ToArray(), allocator);
            chunk._vertices = new NativeArray<float3>(vertexTotal, allocator, NativeArrayOptions.UninitializedMemory);
            chunk._normals = new NativeArray<float3>(vertexTotal, allocator, NativeArrayOptions.UninitializedMemory);
            chunk._outlineNormals = new NativeArray<float3>(vertexTotal, allocator, NativeArrayOptions.UninitializedMemory);
            chunk._tangents = new NativeArray<float4>(vertexTotal, allocator, NativeArrayOptions.UninitializedMemory);
            chunk._uvs = new NativeArray<float2>(vertexTotal, allocator, NativeArrayOptions.UninitializedMemory);
            chunk._indices = new NativeArray<int>(indexTotal, allocator, NativeArrayOptions.UninitializedMemory);
            chunk._colliderVertices = new NativeArray<float3>(colliderVertexTotal, allocator, NativeArrayOptions.UninitializedMemory);
            chunk._triangles = new NativeArray<int3>(triangleTotal, allocator, NativeArrayOptions.UninitializedMemory);
            chunk._builtColliders = new NativeArray<BlobAssetReference<Unity.Physics.Collider>>(colliderBatches.Count, allocator);

            // Jobs: bend meshes, then outline normals; bend colliders, then build them
            var bendMeshes = new BendMeshJob
            {
                Geometry = geometryBlob, Corners = corners, Width = width, Height = height, TileSize = tileSize,
                Instances = chunk._instances, Items = chunk._meshItems,
                Vertices = chunk._vertices, Normals = chunk._normals, Tangents = chunk._tangents, Uvs = chunk._uvs, Indices = chunk._indices,
            }.Schedule(chunk._meshItems.Length, 4);
            var outlines = new OutlineNormalsJob
            {
                Batches = chunk._meshBatches, Vertices = chunk._vertices, Normals = chunk._normals, Tangents = chunk._tangents,
                OutlineNormals = chunk._outlineNormals,
            }.Schedule(chunk._meshBatches.Length, 1, bendMeshes);
            var bendColliders = new BendColliderJob
            {
                Geometry = geometryBlob, Corners = corners, Width = width, Height = height, TileSize = tileSize,
                Instances = chunk._instances, Items = chunk._colliderItems,
                Vertices = chunk._colliderVertices, Triangles = chunk._triangles,
            }.Schedule(chunk._colliderItems.Length, 4);
            var colliders = new CreateCollidersJob
            {
                Batches = chunk._colliderBatches, Vertices = chunk._colliderVertices, Triangles = chunk._triangles,
                Results = chunk._builtColliders,
            }.Schedule(chunk._colliderBatches.Length, 1, bendColliders);

            chunk.Handle = JobHandle.CombineDependencies(outlines, colliders);
            return chunk;
        }

        // A point in the part's own space, turned with the part, placed on the bent grid; its height stays on top
        static float3 Bend(NativeArray<float3> corners, int width, int height, float tileSize, quaternion turn, float2 center, float3 local)
        {
            var turned = math.mul(turn, local);
            var ground = LevelDeformation.Sample(corners, width, height, center.x + turned.x / tileSize, center.y + turned.z / tileSize);
            return ground + new float3(0f, turned.y, 0f);
        }

        /// <summary>
        /// Waits for the jobs (if still running), creates the render and collider entities at the layout's origin and turn,
        /// and frees the job data. Created meshes and colliders are added to the lists so the caller can free them later.
        /// Returns how many render and collider entities were made.
        /// </summary>
        public (int Renderers, int Colliders) Finish(EntityManager entityManager, float3 origin, quaternion rotation, Material[] materials,
                                                     List<Mesh> createdMeshes, List<BlobAssetReference<Unity.Physics.Collider>> createdColliders)
        {
            Handle.Complete();
            var transform = LocalTransform.FromPositionRotation(origin, rotation);
            var localToWorld = new LocalToWorld { Value = float4x4.TRS(origin, rotation, 1f) };

            // Render chunks: one mesh per material, all in one RenderMeshArray
            var meshes = new Mesh[_meshBatches.Length];
            for (int b = 0; b < _meshBatches.Length; b++)
            {
                var batch = _meshBatches[b];
                var mesh = new Mesh { name = "Level chunk", indexFormat = batch.VertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(_vertices, batch.VertexStart, batch.VertexCount);
                mesh.SetNormals(_normals, batch.VertexStart, batch.VertexCount);
                mesh.SetTangents(_tangents, batch.VertexStart, batch.VertexCount);
                mesh.SetUVs(0, _uvs, batch.VertexStart, batch.VertexCount);
                mesh.SetUVs(3, _outlineNormals, batch.VertexStart, batch.VertexCount);
                mesh.SetIndices(_indices, batch.IndexStart, batch.IndexCount, MeshTopology.Triangles, 0);
                mesh.RecalculateBounds();
                meshes[b] = mesh;
                createdMeshes.Add(mesh);
            }
            if (meshes.Length > 0)
            {
                var renderMeshArray = new RenderMeshArray(materials, meshes);
                var description = new RenderMeshDescription(ShadowCastingMode.On, receiveShadows: true);
                for (int b = 0; b < meshes.Length; b++)
                {
                    var entity = entityManager.CreateEntity();
                    entityManager.AddComponentData(entity, transform);
                    entityManager.AddComponentData(entity, localToWorld);
                    RenderMeshUtility.AddComponents(entity, entityManager, description, renderMeshArray,
                                                    MaterialMeshInfo.FromRenderMeshArrayIndices(_meshBatches[b].Material, b));
                }
            }

            // Static collider chunks: one mesh collider per layer and aimability
            int colliderCount = 0;
            for (int b = 0; b < _builtColliders.Length; b++)
            {
                var collider = _builtColliders[b];
                if (!collider.IsCreated) continue;
                createdColliders.Add(collider);

                var entity = entityManager.CreateEntity();
                entityManager.AddComponentData(entity, transform);
                entityManager.AddComponentData(entity, localToWorld);
                entityManager.AddComponentData(entity, new Unity.Physics.PhysicsCollider { Value = collider });
                entityManager.AddSharedComponent(entity, new Unity.Physics.PhysicsWorldIndex());
                if (_colliderBatches[b].Ground) entityManager.AddComponent<Ground>(entity);
                colliderCount++;
            }

            Dispose(disposeColliders: false);
            return (meshes.Length, colliderCount);
        }

        /// <summary>Stops using the chunk without making it (e.g. the level is unloading): waits for the jobs and frees everything.</summary>
        public void Dispose(bool disposeColliders = true)
        {
            Handle.Complete();
            if (disposeColliders && _builtColliders.IsCreated)
                foreach (var collider in _builtColliders) if (collider.IsCreated) collider.Dispose();
            if (_instances.IsCreated) _instances.Dispose();
            if (_meshItems.IsCreated) _meshItems.Dispose();
            if (_colliderItems.IsCreated) _colliderItems.Dispose();
            if (_meshBatches.IsCreated) _meshBatches.Dispose();
            if (_colliderBatches.IsCreated) _colliderBatches.Dispose();
            if (_vertices.IsCreated) _vertices.Dispose();
            if (_normals.IsCreated) _normals.Dispose();
            if (_outlineNormals.IsCreated) _outlineNormals.Dispose();
            if (_tangents.IsCreated) _tangents.Dispose();
            if (_uvs.IsCreated) _uvs.Dispose();
            if (_indices.IsCreated) _indices.Dispose();
            if (_colliderVertices.IsCreated) _colliderVertices.Dispose();
            if (_triangles.IsCreated) _triangles.Dispose();
            if (_builtColliders.IsCreated) _builtColliders.Dispose();
        }

        // ---------- Jobs ----------

        [BurstCompile]
        struct BendMeshJob : IJobParallelFor
        {
            [ReadOnly] public BlobAssetReference<LevelGeometryBlob> Geometry;
            [ReadOnly] public NativeArray<float3> Corners;
            public int Width, Height;
            public float TileSize;
            [ReadOnly] public NativeArray<Instance> Instances;
            [ReadOnly] public NativeArray<MeshItem> Items;

            // Each item writes only its own ranges
            [NativeDisableParallelForRestriction] public NativeArray<float3> Vertices;
            [NativeDisableParallelForRestriction] public NativeArray<float3> Normals;
            [NativeDisableParallelForRestriction] public NativeArray<float4> Tangents;
            [NativeDisableParallelForRestriction] public NativeArray<float2> Uvs;
            [NativeDisableParallelForRestriction] public NativeArray<int> Indices;

            public void Execute(int index)
            {
                var item = Items[index];
                var instance = Instances[item.Instance];
                ref var part = ref Part(ref Geometry.Value, instance);
                ref var mesh = ref part.Meshes[item.MeshPart];
                var turn = quaternion.RotateY(math.radians(90f * instance.Rotation));

                for (int v = 0; v < mesh.Vertices.Length; v++)
                {
                    int target = item.VertexStart + v;
                    Vertices[target] = Bend(Corners, Width, Height, TileSize, turn, instance.Center, mesh.Vertices[v]);
                    Normals[target] = math.mul(turn, mesh.Normals[v]);
                    var tangent = mesh.Tangents[v];
                    Tangents[target] = new float4(math.mul(turn, tangent.xyz), tangent.w);
                    Uvs[target] = mesh.Uvs[v];
                }
                int offset = item.VertexStart - item.BatchVertexStart;
                for (int i = 0; i < mesh.Indices.Length; i++) Indices[item.IndexStart + i] = offset + mesh.Indices[i];
            }
        }

        /// <summary>
        /// Normals averaged over vertices that share a position, in tangent space, for UV3: PerfectLoot/Comic's outline
        /// uses them so hard-edged level geometry gets unbroken ink lines (like ToonOutlineNormalsPostprocessor does for
        /// imported models; merging drops the tiles' own). One mesh batch per index.
        /// </summary>
        [BurstCompile]
        struct OutlineNormalsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<MeshBatch> Batches;
            [ReadOnly] public NativeArray<float3> Vertices;
            [ReadOnly] public NativeArray<float3> Normals;
            [ReadOnly] public NativeArray<float4> Tangents;
            [NativeDisableParallelForRestriction] public NativeArray<float3> OutlineNormals;

            public void Execute(int index)
            {
                var batch = Batches[index];
                var sums = new NativeHashMap<int3, float3>(math.max(1, batch.VertexCount), Allocator.Temp);
                int end = batch.VertexStart + batch.VertexCount;
                for (int v = batch.VertexStart; v < end; v++)
                {
                    var key = (int3)math.round(Vertices[v] * 1000f);
                    sums.TryGetValue(key, out var sum);
                    sums[key] = sum + Normals[v];
                }
                for (int v = batch.VertexStart; v < end; v++)
                {
                    var smooth = math.normalizesafe(sums[(int3)math.round(Vertices[v] * 1000f)]);
                    var normal = Normals[v];
                    var tangent = Tangents[v];
                    var bitangent = math.cross(normal, tangent.xyz) * tangent.w;
                    OutlineNormals[v] = new float3(math.dot(smooth, tangent.xyz), math.dot(smooth, bitangent), math.dot(smooth, normal));
                }
            }
        }

        [BurstCompile]
        struct BendColliderJob : IJobParallelFor
        {
            [ReadOnly] public BlobAssetReference<LevelGeometryBlob> Geometry;
            [ReadOnly] public NativeArray<float3> Corners;
            public int Width, Height;
            public float TileSize;
            [ReadOnly] public NativeArray<Instance> Instances;
            [ReadOnly] public NativeArray<ColliderItem> Items;

            [NativeDisableParallelForRestriction] public NativeArray<float3> Vertices;
            [NativeDisableParallelForRestriction] public NativeArray<int3> Triangles;

            public void Execute(int index)
            {
                var item = Items[index];
                var instance = Instances[item.Instance];
                ref var part = ref Part(ref Geometry.Value, instance);
                ref var collider = ref part.Colliders[item.ColliderPart];
                var turn = quaternion.RotateY(math.radians(90f * instance.Rotation));

                for (int v = 0; v < collider.Vertices.Length; v++)
                    Vertices[item.VertexStart + v] = Bend(Corners, Width, Height, TileSize, turn, instance.Center, collider.Vertices[v]);
                int offset = item.VertexStart - item.BatchVertexStart;
                for (int t = 0; t * 3 + 2 < collider.Indices.Length; t++)
                    Triangles[item.TriangleStart + t] = new int3(offset + collider.Indices[t * 3], offset + collider.Indices[t * 3 + 1], offset + collider.Indices[t * 3 + 2]);
            }
        }

        /// <summary>One physics mesh collider per collider batch (Burst: much faster than building them on the main thread).</summary>
        [BurstCompile]
        struct CreateCollidersJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<ColliderBatch> Batches;
            [ReadOnly] public NativeArray<float3> Vertices;
            [ReadOnly] public NativeArray<int3> Triangles;
            public NativeArray<BlobAssetReference<Unity.Physics.Collider>> Results;

            public void Execute(int index)
            {
                var batch = Batches[index];
                if (batch.TriangleCount == 0) return;
                var filter = new Unity.Physics.CollisionFilter { BelongsTo = 1u << batch.Layer, CollidesWith = batch.CollidesWith };
                Results[index] = Unity.Physics.MeshCollider.Create(Vertices.GetSubArray(batch.VertexStart, batch.VertexCount),
                                                                   Triangles.GetSubArray(batch.TriangleStart, batch.TriangleCount), filter);
            }
        }
    }
}
