using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace LevelGeneration
{
    /// <summary>The meshes, colliders and props of every tile and piece, in their own space (pivot at the footprint's middle).</summary>
    public struct LevelGeometryBlob
    {
        public BlobArray<LevelPartGeometry> Tiles;      // Indexed like WfcTileSet.Tiles
        public BlobArray<LevelPartGeometry> Pieces;     // Indexed like WfcTileSet.Pieces
    }

    public struct LevelPartGeometry
    {
        public BlobArray<LevelMeshPart> Meshes;
        public BlobArray<LevelColliderPart> Colliders;
        public BlobArray<LevelPropPart> Props;
    }

    /// <summary>One submesh: stretched with the level and merged with others using the same material.</summary>
    public struct LevelMeshPart
    {
        public int Material;    // Index into the LevelMaterial buffer
        public BlobArray<float3> Vertices;
        public BlobArray<float2> Uvs;
        public BlobArray<int> Indices;
    }

    /// <summary>One collider as triangles: stretched with the level and merged with others on the same layer.</summary>
    public struct LevelColliderPart
    {
        public int Layer;
        public uint CollidesWith;
        public bool Ground;     // Had GroundAuthoring: aimable ground
        public BlobArray<float3> Vertices;
        public BlobArray<int> Indices;
    }

    /// <summary>A prop (WfcPropAuthoring): spawned whole at this pose, moved with the level but not stretched.</summary>
    public struct LevelPropPart
    {
        public int Prefab;      // Index into the LevelPropPrefab buffer
        public float3 Position;
        public quaternion Rotation;
    }

    /// <summary>Materials of the level meshes (LevelMeshPart.Material).</summary>
    public struct LevelMaterial : IBufferElementData
    {
        public UnityObjectRef<Material> Value;
    }

    /// <summary>Baked prop prefabs (LevelPropPart.Prefab).</summary>
    public struct LevelPropPrefab : IBufferElementData
    {
        public Entity Value;
    }

    /// <summary>
    /// Reads a tile or piece prefab at bake time: every mesh renderer's submeshes and every collider, in the prefab's
    /// space, plus its WfcPropAuthoring props. Anything under a prop marker is left out.
    /// </summary>
    public static class LevelGeometryExtractor
    {
        public class Part
        {
            public readonly List<MeshPiece> Meshes = new List<MeshPiece>();
            public readonly List<ColliderPiece> Colliders = new List<ColliderPiece>();
            public readonly List<PropPiece> Props = new List<PropPiece>();
        }

        public class MeshPiece
        {
            public int Material;
            public Vector3[] Vertices;
            public Vector2[] Uvs;
            public int[] Indices;
        }

        public class ColliderPiece
        {
            public int Layer;
            public uint CollidesWith;
            public bool Ground;
            public Vector3[] Vertices;
            public int[] Indices;
        }

        public struct PropPiece
        {
            public GameObject Prefab;
            public Vector3 Position;
            public Quaternion Rotation;
        }

        /// <summary>The prefab's geometry. Materials are added to (and indexed in) materials; problems go to warnings.</summary>
        public static Part Extract(GameObject prefab, List<Material> materials, List<string> warnings)
        {
            var part = new Part();
            if (prefab == null) return part;
            var root = prefab.transform;
            var toRoot = root.worldToLocalMatrix;

            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (UnderProp(transform, root)) continue;
                var matrix = toRoot * transform.localToWorldMatrix;
                var go = transform.gameObject;

                if (go.TryGetComponent<MeshFilter>(out var filter) && go.TryGetComponent<MeshRenderer>(out var renderer) && filter.sharedMesh != null)
                    AddMesh(part, filter.sharedMesh, renderer.sharedMaterials, matrix, materials, warnings, prefab.name);

                foreach (var collider in go.GetComponents<Collider>())
                    if (collider != null && collider.enabled && !collider.isTrigger)
                        AddCollider(part, collider, matrix, warnings, prefab.name);
            }

            foreach (var prop in prefab.GetComponentsInChildren<WfcPropAuthoring>(true))
            {
                if (prop.Prefab == null) continue;
                var matrix = toRoot * prop.transform.localToWorldMatrix;
                part.Props.Add(new PropPiece { Prefab = prop.Prefab, Position = matrix.GetPosition(), Rotation = matrix.rotation });
            }
            return part;
        }

        static bool UnderProp(Transform transform, Transform root)
        {
            for (var current = transform; current != null; current = current.parent)
            {
                if (current.GetComponent<WfcPropAuthoring>() != null) return true;
                if (current == root) break;
            }
            return false;
        }

        static void AddMesh(Part part, Mesh mesh, Material[] renderMaterials, Matrix4x4 matrix, List<Material> materials, List<string> warnings, string owner)
        {
            var vertices = mesh.vertices;
            if (vertices.Length == 0)
            {
                warnings.Add($"{owner}: can't read mesh {mesh.name}. Enable Read/Write in its import settings.");
                return;
            }
            var uvs = mesh.uv;
            var moved = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++) moved[i] = matrix.MultiplyPoint3x4(vertices[i]);
            if (uvs.Length != vertices.Length) uvs = new Vector2[vertices.Length];
            bool mirrored = matrix.determinant < 0f;

            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                var material = renderMaterials.Length > 0 ? renderMaterials[Mathf.Min(s, renderMaterials.Length - 1)] : null;
                if (material == null) continue;
                int index = materials.IndexOf(material);
                if (index < 0) { index = materials.Count; materials.Add(material); }

                var indices = mesh.GetTriangles(s);
                if (mirrored) FlipWinding(indices);
                part.Meshes.Add(new MeshPiece { Material = index, Vertices = moved, Uvs = uvs, Indices = indices });
            }
        }

        static void AddCollider(Part part, Collider collider, Matrix4x4 matrix, List<string> warnings, string owner)
        {
            Vector3[] vertices;
            int[] indices;
            switch (collider)
            {
                case MeshCollider meshCollider when meshCollider.sharedMesh != null:
                    vertices = meshCollider.sharedMesh.vertices;
                    indices = meshCollider.sharedMesh.triangles;
                    break;
                case BoxCollider box:
                    BoxTriangles(box.center, box.size, out vertices, out indices);
                    break;
                case SphereCollider sphere:
                    BoxTriangles(sphere.center, Vector3.one * sphere.radius * 2f, out vertices, out indices);
                    break;
                case CapsuleCollider capsule:
                    var size = Vector3.one * capsule.radius * 2f;
                    size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
                    BoxTriangles(capsule.center, size, out vertices, out indices);
                    break;
                default:
                    warnings.Add($"{owner}: collider {collider.GetType().Name} on {collider.name} isn't supported and is left out.");
                    return;
            }
            if (vertices.Length == 0) return;

            var moved = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++) moved[i] = matrix.MultiplyPoint3x4(vertices[i]);
            if (matrix.determinant < 0f) FlipWinding(indices);

            int layer = collider.gameObject.layer;
            uint collidesWith = 0;
            for (int other = 0; other < 32; other++)
                if (!Physics.GetIgnoreLayerCollision(layer, other)) collidesWith |= 1u << other;

            part.Colliders.Add(new ColliderPiece
            {
                Layer = layer,
                CollidesWith = collidesWith,
                Ground = collider.GetComponent<GroundAuthoring>() != null,
                Vertices = moved,
                Indices = indices,
            });
        }

        // A box as 8 corners and 12 outward-facing triangles (clockwise seen from outside, Unity's front face)
        static void BoxTriangles(Vector3 center, Vector3 size, out Vector3[] vertices, out int[] indices)
        {
            var h = size * 0.5f;
            vertices = new[]
            {
                center + new Vector3(-h.x, -h.y, -h.z), center + new Vector3(h.x, -h.y, -h.z),
                center + new Vector3(h.x, h.y, -h.z), center + new Vector3(-h.x, h.y, -h.z),
                center + new Vector3(-h.x, -h.y, h.z), center + new Vector3(h.x, -h.y, h.z),
                center + new Vector3(h.x, h.y, h.z), center + new Vector3(-h.x, h.y, h.z),
            };
            indices = new[]
            {
                0, 2, 1, 0, 3, 2,   // -Z
                5, 6, 4, 4, 6, 7,   // +Z
                4, 7, 0, 0, 7, 3,   // -X
                1, 2, 5, 5, 2, 6,   // +X
                3, 7, 2, 2, 7, 6,   // +Y
                4, 0, 5, 5, 0, 1,   // -Y
            };
        }

        static void FlipWinding(int[] indices)
        {
            for (int i = 0; i + 2 < indices.Length; i += 3) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
        }

        /// <summary>Writes an extracted part into the blob. propIndex maps a prop prefab to its LevelPropPrefab index.</summary>
        public static void Write(BlobBuilder builder, ref LevelPartGeometry target, Part part, System.Func<GameObject, int> propIndex)
        {
            var meshes = builder.Allocate(ref target.Meshes, part.Meshes.Count);
            for (int i = 0; i < part.Meshes.Count; i++)
            {
                var source = part.Meshes[i];
                meshes[i].Material = source.Material;
                var vertices = builder.Allocate(ref meshes[i].Vertices, source.Vertices.Length);
                var uvs = builder.Allocate(ref meshes[i].Uvs, source.Uvs.Length);
                for (int v = 0; v < source.Vertices.Length; v++) { vertices[v] = source.Vertices[v]; uvs[v] = source.Uvs[v]; }
                var indices = builder.Allocate(ref meshes[i].Indices, source.Indices.Length);
                for (int n = 0; n < source.Indices.Length; n++) indices[n] = source.Indices[n];
            }

            var colliders = builder.Allocate(ref target.Colliders, part.Colliders.Count);
            for (int i = 0; i < part.Colliders.Count; i++)
            {
                var source = part.Colliders[i];
                colliders[i].Layer = source.Layer;
                colliders[i].CollidesWith = source.CollidesWith;
                colliders[i].Ground = source.Ground;
                var vertices = builder.Allocate(ref colliders[i].Vertices, source.Vertices.Length);
                for (int v = 0; v < source.Vertices.Length; v++) vertices[v] = source.Vertices[v];
                var indices = builder.Allocate(ref colliders[i].Indices, source.Indices.Length);
                for (int n = 0; n < source.Indices.Length; n++) indices[n] = source.Indices[n];
            }

            var props = builder.Allocate(ref target.Props, part.Props.Count);
            for (int i = 0; i < part.Props.Count; i++)
                props[i] = new LevelPropPart { Prefab = propIndex(part.Props[i].Prefab), Position = part.Props[i].Position, Rotation = part.Props[i].Rotation };
        }
    }
}
