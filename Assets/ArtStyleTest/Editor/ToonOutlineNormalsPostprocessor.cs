using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PerfectLoot.ArtStyleTest.Editor
{
    /// <summary>
    /// For models imported under Assets/ArtStyleTest/Models, averages normals across
    /// vertices that share a position and stores the result (in tangent space) in UV3.
    /// PerfectLoot/Toon's outline pass uses it so flat-shaded/hard-edged meshes get
    /// unbroken outlines. Tangent space keeps it correct for skinned meshes too.
    /// </summary>
    public class ToonOutlineNormalsPostprocessor : AssetPostprocessor
    {
        const string Folder = "Assets/ArtStyleTest/Models";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var importer = (ModelImporter)assetImporter;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.globalScale = 1f;
        }

        void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.StartsWith(Folder)) return;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh) Bake(mf.sharedMesh);
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh) Bake(smr.sharedMesh);
        }

        static void Bake(Mesh mesh)
        {
            var verts = mesh.vertices;
            var normals = mesh.normals;
            var tangents = mesh.tangents;
            if (normals == null || normals.Length != verts.Length) return;

            var sum = new Dictionary<Vector3Int, Vector3>();
            Vector3Int Key(Vector3 p) => new Vector3Int(
                Mathf.RoundToInt(p.x * 10000f), Mathf.RoundToInt(p.y * 10000f), Mathf.RoundToInt(p.z * 10000f));

            for (int i = 0; i < verts.Length; i++)
            {
                var k = Key(verts[i]);
                sum.TryGetValue(k, out var s);
                sum[k] = s + normals[i];
            }

            var smooth = new List<Vector3>(verts.Length);
            bool hasTangents = tangents != null && tangents.Length == verts.Length;
            for (int i = 0; i < verts.Length; i++)
            {
                var s = sum[Key(verts[i])].normalized;
                if (!hasTangents) { smooth.Add(new Vector3(0, 0, 1)); continue; }
                var n = normals[i];
                var t = (Vector3)tangents[i];
                var b = Vector3.Cross(n, t) * tangents[i].w;
                smooth.Add(new Vector3(Vector3.Dot(s, t), Vector3.Dot(s, b), Vector3.Dot(s, n)));
            }
            mesh.SetUVs(3, smooth);
        }
    }
}
