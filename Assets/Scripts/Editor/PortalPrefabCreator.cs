using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

/// <summary>
/// Builds the Portal prefab (a placeholder glowing ring with a ring on the ground showing its range; set its Scene) and
/// the PortalHUD prefab (add it to every game scene). Run it again to rebuild them; their GUIDs, and those of the
/// generated meshes and materials, are kept, so placed portals and placement sets keep working.
/// </summary>
public static class PortalPrefabCreator
{
    const string Folder = "Assets/Prefabs/Portal";
    const string PortalPath = "Assets/Prefabs/Portal.prefab";
    const string HudPath = "Assets/Prefabs/PortalHUD.prefab";
    const string UxmlPath = "Assets/UI/Portal/PortalHUD.uxml";
    const string PanelSettingsPath = "Assets/UI/PanelSettings.asset";

    const float RingRadius = 1.2f;
    const float RingThickness = 0.12f;
    const float RingCenterHeight = RingRadius + RingThickness;
    const float RangeRingWidth = 0.04f;     // Of the radius-1 mesh, so 12 cm at the default 3 m range

    [MenuItem("Tools/Create Portal Prefabs")]
    static void Create()
    {
        var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
        var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (uxml == null || panelSettings == null || lit == null || unlit == null)
        {
            Debug.LogError($"Create Portal Prefabs: missing {UxmlPath}, {PanelSettingsPath} or the URP Lit/Unlit shaders.");
            return;
        }
        bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(PortalPath) != null || AssetDatabase.LoadAssetAtPath<GameObject>(HudPath) != null;
        if (exists && !EditorUtility.DisplayDialog("Create Portal Prefabs", $"Rebuild {PortalPath} and {HudPath}?", "Rebuild", "Cancel"))
            return;

        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Prefabs", "Portal");
        CreatePortal(lit, unlit);
        CreateHud(uxml, panelSettings);
        AssetDatabase.SaveAssets();
        Debug.Log($"Created {PortalPath} (set its Scene) and {HudPath} (add it to every game scene).");
    }

    static void CreatePortal(Shader lit, Shader unlit)
    {
        var ringMaterial = new Material(lit) { name = "PortalRing" };
        ringMaterial.SetColor("_BaseColor", new Color(0.55f, 0.3f, 1f));
        ringMaterial.EnableKeyword("_EMISSION");
        ringMaterial.SetColor("_EmissionColor", new Color(0.55f, 0.3f, 1f) * 2.5f);
        ringMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        ringMaterial = SaveOrReplace(ringMaterial, $"{Folder}/PortalRing.mat");

        var surfaceMaterial = new Material(unlit) { name = "PortalSurface" };
        surfaceMaterial.SetColor("_BaseColor", new Color(0.22f, 0.08f, 0.4f));
        surfaceMaterial = SaveOrReplace(surfaceMaterial, $"{Folder}/PortalSurface.mat");

        var rangeMaterial = new Material(unlit) { name = "PortalRange" };
        rangeMaterial.SetColor("_BaseColor", new Color(0.4f, 0.3f, 0.7f));
        rangeMaterial = SaveOrReplace(rangeMaterial, $"{Folder}/PortalRange.mat");

        var ringMesh = SaveOrReplace(Torus(RingRadius, RingThickness, 48, 12), $"{Folder}/PortalRing.asset");
        var surfaceMesh = SaveOrReplace(Disc(RingRadius - RingThickness * 0.5f, 48), $"{Folder}/PortalSurface.asset");
        var rangeMesh = SaveOrReplace(Annulus(1f - RangeRingWidth, 1f, 96), $"{Folder}/PortalRange.asset");

        // No colliders: the portal mustn't block movement or the mouse raycasts
        var root = new GameObject("Portal");
        var authoring = root.AddComponent<PortalAuthoring>();
        AddPart(root, "Ring", ringMesh, ringMaterial, new Vector3(0f, RingCenterHeight, 0f), Vector3.one, ShadowCastingMode.On);
        AddPart(root, "Surface", surfaceMesh, surfaceMaterial, new Vector3(0f, RingCenterHeight, 0f), Vector3.one, ShadowCastingMode.Off);
        authoring.RangeRing = AddPart(root, "RangeRing", rangeMesh, rangeMaterial, new Vector3(0f, 0.05f, 0f),
                                      new Vector3(authoring.Range, 1f, authoring.Range), ShadowCastingMode.Off);

        PrefabUtility.SaveAsPrefabAsset(root, PortalPath);
        Object.DestroyImmediate(root);
    }

    static Transform AddPart(GameObject parent, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale,
                             ShadowCastingMode shadows)
    {
        var part = new GameObject(name);
        part.transform.SetParent(parent.transform, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = part.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = shadows;
        return part.transform;
    }

    static void CreateHud(VisualTreeAsset uxml, PanelSettings panelSettings)
    {
        var hud = new GameObject("PortalHUD");
        hud.AddComponent<PortalHUD>();      // Adds the PanelRenderer
        var renderer = new SerializedObject(hud.GetComponent<PanelRenderer>());
        var settingsProperty = renderer.FindProperty("m_PanelSettings");
        var sourceProperty = renderer.FindProperty("sourceAsset");
        if (settingsProperty != null) settingsProperty.objectReferenceValue = panelSettings;
        if (sourceProperty != null) sourceProperty.objectReferenceValue = uxml;
        renderer.ApplyModifiedPropertiesWithoutUndo();
        if (settingsProperty == null || sourceProperty == null)
            Debug.LogWarning($"Create Portal Prefabs: set PortalHUD's Panel Settings and Source Asset by hand ({PanelSettingsPath}, {UxmlPath}).");

        PrefabUtility.SaveAsPrefabAsset(hud, HudPath);
        Object.DestroyImmediate(hud);
    }

    // Replaces an existing asset's contents rather than the asset, so references to it stay valid
    static T SaveOrReplace<T>(T asset, string path) where T : Object
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
        EditorUtility.CopySerialized(asset, existing);
        existing.name = System.IO.Path.GetFileNameWithoutExtension(path);
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(asset);
        return existing;
    }

    // ---------- Meshes ----------

    // Upright ring around the Z axis (facing along Z)
    static Mesh Torus(float radius, float thickness, int segments, int sides)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();
        for (int i = 0; i <= segments; i++)
        {
            float u = i * Mathf.PI * 2f / segments;
            var center = new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0f) * radius;
            for (int j = 0; j <= sides; j++)
            {
                float v = j * Mathf.PI * 2f / sides;
                var normal = new Vector3(Mathf.Cos(u) * Mathf.Cos(v), Mathf.Sin(u) * Mathf.Cos(v), Mathf.Sin(v));
                vertices.Add(center + normal * thickness);
                normals.Add(normal);
            }
        }
        for (int i = 0; i < segments; i++)
        {
            for (int j = 0; j < sides; j++)
            {
                int a = i * (sides + 1) + j, b = a + sides + 1, c = b + 1, d = a + 1;
                triangles.AddRange(new[] { a, b, d, b, c, d });
            }
        }
        return BuildMesh("PortalRing", vertices, normals, triangles);
    }

    // Upright disc facing both ways along Z
    static Mesh Disc(float radius, int segments)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();
        foreach (float side in new[] { 1f, -1f })
        {
            int center = vertices.Count;
            vertices.Add(Vector3.zero);
            normals.Add(new Vector3(0f, 0f, side));
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                vertices.Add(new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius);
                normals.Add(new Vector3(0f, 0f, side));
            }
            for (int i = 0; i < segments; i++)
            {
                int k = center + 1 + i;
                if (side > 0f) triangles.AddRange(new[] { center, k, k + 1 });
                else triangles.AddRange(new[] { center, k + 1, k });
            }
        }
        return BuildMesh("PortalSurface", vertices, normals, triangles);
    }

    // Flat ring on the ground, facing up
    static Mesh Annulus(float inner, float outer, int segments)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();
        for (int i = 0; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            vertices.Add(direction * inner);
            vertices.Add(direction * outer);
            normals.Add(Vector3.up);
            normals.Add(Vector3.up);
        }
        for (int i = 0; i < segments; i++)
        {
            int innerK = i * 2, outerK = innerK + 1, innerNext = innerK + 2, outerNext = innerK + 3;
            triangles.AddRange(new[] { innerK, outerNext, outerK, innerK, innerNext, outerNext });
        }
        return BuildMesh("PortalRange", vertices, normals, triangles);
    }

    static Mesh BuildMesh(string name, List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
    {
        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
