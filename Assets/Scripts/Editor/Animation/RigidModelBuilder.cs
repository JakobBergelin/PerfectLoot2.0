using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns a rigidly skinned FBX (every vertex on one bone, clips named after RigidClip) into an ECS-friendly visual prefab:
/// one plain mesh per bone parented to a copy of the bone hierarchy, a RigidAnimationSet with every clip sampled at
/// SampleRate, and the collider, rigidbody and bars of a visuals template (EnemyVisuals / PlayerVisuals).
/// Entities Graphics can't skin with PL_Comic, so the parts are moved rather than deformed.
/// </summary>
public static class RigidModelBuilder
{
    const float SampleRate = 30f;
    const float CapsuleHalfHeight = 1f;     // Visual prefabs have a 2 m capsule centered on the root

    public class Settings
    {
        public string Fbx;
        public Material[] Materials;        // In the model's material slot (submesh) order
        public GameObject Template;
        public string Folder;
        public string Name;
    }

    public static GameObject Build(Settings settings)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(settings.Fbx);
        if (importer == null) throw new FileNotFoundException(settings.Fbx);
        if (!importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(settings.Fbx);
        var skinned = model.GetComponentInChildren<SkinnedMeshRenderer>();
        if (skinned == null) throw new Exception($"{settings.Fbx} has no SkinnedMeshRenderer.");

        // Every transform but the root and the mesh object, parents before children
        var bones = model.GetComponentsInChildren<Transform>(true).Where(t => t != model.transform && t != skinned.transform).ToList();
        var paths = bones.Select(t => AnimationUtility.CalculateTransformPath(t, model.transform)).ToList();

        Directory.CreateDirectory(settings.Folder);
        string setPath = $"{settings.Folder}/{settings.Name}_Rig.asset";
        var set = AssetDatabase.LoadAssetAtPath<RigidAnimationSet>(setPath);
        if (set == null)
        {
            set = ScriptableObject.CreateInstance<RigidAnimationSet>();
            AssetDatabase.CreateAsset(set, setPath);
        }
        set.Bones = paths;
        set.Clips = SampleClips(settings.Fbx, model, paths);

        foreach (var old in AssetDatabase.LoadAllAssetRepresentationsAtPath(setPath).OfType<Mesh>()) UnityEngine.Object.DestroyImmediate(old, true);
        var parts = SplitByBone(skinned, settings.Materials);
        foreach (var part in parts.Values) AssetDatabase.AddObjectToAsset(part.Mesh, set);
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();

        return SavePrefab(settings, model, skinned, bones, parts, set);
    }

    static List<RigidAnimationSet.Clip> SampleClips(string fbx, GameObject model, List<string> paths)
    {
        var clips = new List<RigidAnimationSet.Clip>();
        foreach (var source in AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>())
        {
            if (source.name.StartsWith("__preview__") || !Enum.TryParse<RigidClip>(source.name, out var kind)) continue;

            // A fresh copy per clip, so curves a clip doesn't have keep their rest pose
            var instance = UnityEngine.Object.Instantiate(model);
            try
            {
                var transforms = paths.Select(p => instance.transform.Find(p)).ToArray();
                int frames = Mathf.Max(1, Mathf.CeilToInt(source.length * SampleRate) + 1);
                var clip = new RigidAnimationSet.Clip { Kind = kind, Length = source.length, SampleRate = SampleRate, Loop = source.isLooping, Frames = frames };
                for (int f = 0; f < frames; f++)
                {
                    source.SampleAnimation(instance, Mathf.Min(f / SampleRate, source.length));
                    foreach (var t in transforms)
                    {
                        clip.Positions.Add(t.localPosition);
                        clip.Rotations.Add(t.localRotation);
                        clip.Scales.Add(t.localScale.x);
                    }
                }
                clips.Add(clip);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }
        return clips.OrderBy(c => c.Kind).ToList();
    }

    class Part
    {
        public Mesh Mesh;
        public Material[] Materials;
    }

    // One mesh per bone in that bone's space (its bind pose), keeping normals, tangents, UVs and the outline normals in UV3
    static Dictionary<Transform, Part> SplitByBone(SkinnedMeshRenderer skinned, Material[] materials)
    {
        var mesh = skinned.sharedMesh;
        var vertices = mesh.vertices;
        var normals = mesh.normals;
        var tangents = mesh.tangents;
        var uv0 = new List<Vector2>(); mesh.GetUVs(0, uv0);
        var uv3 = new List<Vector3>(); mesh.GetUVs(3, uv3);
        var weights = mesh.boneWeights;
        var bindposes = mesh.bindposes;

        var parts = new Dictionary<Transform, Part>();
        for (int b = 0; b < skinned.bones.Length; b++)
        {
            var bind = bindposes[b];
            var map = new Dictionary<int, int>();
            var partVertices = new List<Vector3>();
            var partNormals = new List<Vector3>();
            var partTangents = new List<Vector4>();
            var partUv0 = new List<Vector2>();
            var partUv3 = new List<Vector3>();
            var submeshes = new List<(int Slot, List<int> Indices)>();

            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var source = mesh.GetTriangles(s);
                var indices = new List<int>();
                for (int i = 0; i + 2 < source.Length; i += 3)
                {
                    if (weights[source[i]].boneIndex0 != b) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        int v = source[i + k];
                        if (!map.TryGetValue(v, out int index))
                        {
                            index = partVertices.Count;
                            map[v] = index;
                            partVertices.Add(bind.MultiplyPoint3x4(vertices[v]));
                            partNormals.Add(bind.MultiplyVector(normals[v]).normalized);
                            var t = bind.MultiplyVector(tangents[v]).normalized;
                            partTangents.Add(new Vector4(t.x, t.y, t.z, tangents[v].w));
                            partUv0.Add(v < uv0.Count ? uv0[v] : Vector2.zero);
                            partUv3.Add(v < uv3.Count ? uv3[v] : new Vector3(0f, 0f, 1f));
                        }
                        indices.Add(index);
                    }
                }
                if (indices.Count > 0) submeshes.Add((s, indices));
            }
            if (submeshes.Count == 0) continue;

            var bone = skinned.bones[b];
            var part = new Mesh { name = bone.name };
            part.SetVertices(partVertices);
            part.SetNormals(partNormals);
            part.SetTangents(partTangents);
            part.SetUVs(0, partUv0);
            part.SetUVs(3, partUv3);
            part.subMeshCount = submeshes.Count;
            for (int s = 0; s < submeshes.Count; s++) part.SetTriangles(submeshes[s].Indices, s);
            part.RecalculateBounds();
            parts[bone] = new Part
            {
                Mesh = part,
                Materials = submeshes.Select(sm => materials[Mathf.Min(sm.Slot, materials.Length - 1)]).ToArray(),
            };
        }
        return parts;
    }

    static GameObject SavePrefab(Settings settings, GameObject model, SkinnedMeshRenderer skinned, List<Transform> sourceBones,
                                 Dictionary<Transform, Part> parts, RigidAnimationSet set)
    {
        var root = (GameObject)PrefabUtility.InstantiatePrefab(settings.Template);
        PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        root.name = settings.Name;
        root.transform.position = Vector3.zero;
        // The template's capsule mesh is only a stand-in for the model
        if (root.TryGetComponent<MeshRenderer>(out var capsuleRenderer)) UnityEngine.Object.DestroyImmediate(capsuleRenderer);
        if (root.TryGetComponent<MeshFilter>(out var capsuleFilter)) UnityEngine.Object.DestroyImmediate(capsuleFilter);

        // The model's feet at the bottom of the capsule
        var modelRoot = new GameObject("Model");
        modelRoot.layer = root.layer;
        modelRoot.transform.SetParent(root.transform, false);
        modelRoot.transform.localPosition = new Vector3(0f, -CapsuleHalfHeight, 0f);

        var copies = new Dictionary<Transform, Transform> { [model.transform] = modelRoot.transform };
        var bones = new Transform[sourceBones.Count];
        float top = 0f;
        for (int i = 0; i < sourceBones.Count; i++)
        {
            var source = sourceBones[i];
            var copy = new GameObject(source.name) { layer = root.layer }.transform;
            copy.SetParent(copies[source.parent], false);
            copy.localPosition = source.localPosition;
            copy.localRotation = source.localRotation;
            copy.localScale = source.localScale;
            copies[source] = copy;
            bones[i] = copy;

            if (parts.TryGetValue(source, out var part))
            {
                copy.gameObject.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
                copy.gameObject.AddComponent<MeshRenderer>().sharedMaterials = part.Materials;
            }
        }
        foreach (var renderer in modelRoot.GetComponentsInChildren<MeshRenderer>())
            top = Mathf.Max(top, renderer.bounds.max.y - modelRoot.transform.position.y);

        // Bars just above the head
        foreach (var name in new[] { "HealthBar/Stick", "ResourceBar/Stick" })
        {
            var stick = root.transform.Find(name);
            if (stick == null) continue;
            var position = stick.localPosition;
            position.y = top - CapsuleHalfHeight + (name.StartsWith("Health") ? 0.35f : 0.05f);
            stick.localPosition = position;
        }

        var authoring = root.AddComponent<RigidAnimationAuthoring>();
        authoring.Animation = set;
        authoring.Bones = bones;

        string path = $"{settings.Folder}/{settings.Name}.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }
}
