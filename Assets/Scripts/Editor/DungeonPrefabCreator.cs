using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds the dungeon markers painted on level layouts with the Prefab brush: Dungeon Exit (a ring on the ground showing
/// its range) and Boss Spawn Point (nothing visible). Run it again to rebuild them; their GUIDs, and those of the
/// generated meshes and materials, are kept, so painted markers keep working.
/// </summary>
public static class DungeonPrefabCreator
{
    const string Folder = "Assets/Prefabs/Dungeon";
    const string ExitPath = Folder + "/DungeonExit.prefab";
    const string BossSpawnPath = Folder + "/BossSpawnPoint.prefab";

    const float RingWidth = 0.06f;      // Of the radius-1 mesh, so 18 cm at the default 3 m range

    [MenuItem("Tools/Create Dungeon Prefabs")]
    static void Create()
    {
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlit == null)
        {
            Debug.LogError("Create Dungeon Prefabs: missing the URP Unlit shader.");
            return;
        }
        bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(ExitPath) != null || AssetDatabase.LoadAssetAtPath<GameObject>(BossSpawnPath) != null;
        if (exists && !EditorUtility.DisplayDialog("Create Dungeon Prefabs", $"Rebuild {ExitPath} and {BossSpawnPath}?", "Rebuild", "Cancel"))
            return;

        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Prefabs", "Dungeon");
        CreateExit(unlit);
        CreateBossSpawn();
        AssetDatabase.SaveAssets();
        Debug.Log($"Created {ExitPath} and {BossSpawnPath}: paint them on a dungeon's level layout with the Prefab brush.");
    }

    static void CreateExit(Shader unlit)
    {
        var material = new Material(unlit) { name = "DungeonExit" };
        material.SetColor("_BaseColor", new Color(1f, 0.8f, 0.3f));
        material = PortalPrefabCreator.SaveOrReplace(material, $"{Folder}/DungeonExit.mat");
        var mesh = PortalPrefabCreator.SaveOrReplace(PortalPrefabCreator.Annulus(1f - RingWidth, 1f, 96), $"{Folder}/DungeonExitRing.asset");
        mesh.name = "DungeonExitRing";

        // No collider: the exit mustn't block movement or the mouse raycasts
        var root = new GameObject("DungeonExit");
        var authoring = root.AddComponent<DungeonExitAuthoring>();
        var ring = new GameObject("RangeRing");
        ring.transform.SetParent(root.transform, false);
        ring.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        ring.transform.localScale = new Vector3(authoring.Range, 1f, authoring.Range);
        ring.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = ring.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        authoring.RangeRing = ring.transform;

        PrefabUtility.SaveAsPrefabAsset(root, ExitPath);
        Object.DestroyImmediate(root);
    }

    static void CreateBossSpawn()
    {
        var root = new GameObject("BossSpawnPoint");
        root.AddComponent<DungeonBossSpawnAuthoring>();
        PrefabUtility.SaveAsPrefabAsset(root, BossSpawnPath);
        Object.DestroyImmediate(root);
    }
}
