using System.Collections.Generic;
using System.Linq;
using Enemies;
using LevelGeneration;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the Hub (cozy room with portals) and the two test levels the Hub connects to, as copies of the HauntedCathedral
/// scene pair: scene with lighting, CoreEntities and an Entities subscene holding a LevelLayout. Run it again to rebuild
/// their content; the scenes' GUIDs are kept. All positions are in meters: grids start at the world origin, unbent.
/// </summary>
public static class HubLevelsCreator
{
    const string TemplateScene = "Assets/Scenes/HauntedCathedral.unity";
    const string TemplateSubscene = "Assets/Scenes/HauntedCathedral/Entities.unity";
    const string PortalPrefab = "Assets/Prefabs/Portal.prefab";
    const string TileSetPath = "Assets/Data/Level/Cathedral/CathedralTileSet.asset";
    const string PropFolder = "Assets/Data/Level/Cathedral/Props/CT_Prop_";
    const string EnemyFolder = "Assets/Data/Enemies/";

    static readonly Color CandleColor = new Color(1f, 0.68f, 0.38f);

    [MenuItem("Tools/Create Hub And Test Levels")]
    static void Create()
    {
        string openScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
        foreach (string name in new[] { "Hub", "TestArena", "TestSwarm" })
        {
            CopyTemplate(name);
            AddToBuildSettings($"Assets/Scenes/{name}.unity");
        }

        BuildHub();
        BuildArena();
        BuildSwarm();
        AssetDatabase.SaveAssets();
        if (!string.IsNullOrEmpty(openScene)) EditorSceneManager.OpenScene(openScene);
        Debug.Log("Created the Hub, TestArena and TestSwarm scenes.");
    }

    static void BuildHub()
    {
        var (scene, layout) = OpenLayout("Hub", 9, 9, new Vector2Int(4, 2), 11);
        AddPortal(scene, "HauntedCathedral", "Haunted Cathedral", 22.5f, 36f);
        AddPortal(scene, "TestArena", "Test Arena", 11f, 30f);
        AddPortal(scene, "TestSwarm", "Test Swarm", 34f, 30f);
        AddPortal(scene, "SampleScene", "Old Town", 11f, 14f);

        foreach (var (x, z) in new[] { (19f, 36f), (26f, 36f), (7.5f, 30f), (14.5f, 30f), (30.5f, 30f), (37.5f, 30f), (7.5f, 14f), (14.5f, 14f) })
            AddProp(scene, "Candelabra", x, z);
        foreach (var (x, z) in new[] { (15f, 18f), (30f, 18f), (15f, 26f), (30f, 26f) })
            AddProp(scene, "Pillar", x, z);
        foreach (var (x, z) in new[] { (19f, 20f), (26f, 20f), (19f, 24f), (26f, 24f) })
            AddProp(scene, "Pews", x, z);
        AddProp(scene, "Tombs_1", 35f, 10f);
        AddProp(scene, "Tombs_2", 38f, 15f, 90f);
        AddProp(scene, "Rubble", 7f, 7f);
        AddProp(scene, "Rubble", 38f, 7f, 180f);
        SaveLayout(scene, layout);

        var main = EditorSceneManager.OpenScene("Assets/Scenes/Hub.unity", OpenSceneMode.Single);
        UseSubscene(main, "Hub");
        foreach (var root in main.GetRootGameObjects())
            if (root.name.StartsWith("Candle Light")) Object.DestroyImmediate(root);

        // Evening indoors: the sky barely counts, the candles light the room
        RenderSettings.ambientSkyColor = new Color(0.34f, 0.27f, 0.27f);
        RenderSettings.ambientEquatorColor = new Color(0.26f, 0.19f, 0.17f);
        RenderSettings.ambientGroundColor = new Color(0.14f, 0.10f, 0.09f);
        RenderSettings.fogColor = new Color(0.16f, 0.11f, 0.10f);
        foreach (var light in Object.FindObjectsByType<Light>())
            if (light.type == LightType.Directional)
            {
                light.color = new Color(1f, 0.82f, 0.68f);
                light.intensity = 0.45f;
            }

        foreach (var (x, z) in new[] { (22.5f, 35f), (11f, 29f), (34f, 29f), (11f, 15f), (22.5f, 22f), (22.5f, 11f), (34f, 13f) })
            AddCandleLight(x, z);
        EditorSceneManager.SaveScene(main);
    }

    // Dummies around the start to try skills on, each enemy type a short walk away so they come one group at a time
    static void BuildArena()
    {
        var (scene, layout) = OpenLayout("TestArena", 13, 13, new Vector2Int(6, 2), 12);
        AddPortal(scene, "Hub", "Hub", 25f, 8f);
        AddProp(scene, "Candelabra", 21.5f, 8f);
        AddProp(scene, "Candelabra", 28.5f, 8f);

        AddSpawner(layout, 3, 4, 35f, 3f, ("Dummy", 2));
        AddSpawner(layout, 6, 5, 35f, 3f, ("Dummy", 3));
        AddSpawner(layout, 9, 4, 35f, 3f, ("Dummy", 2));
        AddSpawner(layout, 3, 9, 16f, 4f, ("Skeleton", 4), ("SkeletonArcher", 2));
        AddSpawner(layout, 6, 10, 16f, 4f, ("Zombie", 4), ("Spider", 4));
        AddSpawner(layout, 9, 9, 16f, 4f, ("Cathedral/Wraith", 3), ("Cathedral/Revenant", 2), ("Cathedral/BellRinger", 1));
        AddSpawner(layout, 11, 11, 12f, 2f, ("Cathedral/RevenantElite", 1));
        SaveLayout(scene, layout);
        UseSubscene(EditorSceneManager.OpenScene("Assets/Scenes/TestArena.unity", OpenSceneMode.Single), "TestArena");
        EditorSceneManager.SaveOpenScenes();
    }

    // Twelve groups of 40, all within reach of the start area, for performance and area skills
    static void BuildSwarm()
    {
        var (scene, layout) = OpenLayout("TestSwarm", 20, 20, new Vector2Int(10, 3), 13);
        AddPortal(scene, "Hub", "Hub", 58f, 12f);
        AddProp(scene, "Candelabra", 54.5f, 12f);
        AddProp(scene, "Candelabra", 61.5f, 12f);

        for (int row = 0; row < 3; row++)
            for (int column = 0; column < 4; column++)
                AddSpawner(layout, 4 + column * 4, 8 + row * 4, 40f, 8f, ("Skeleton", 20), ("Zombie", 12), ("Spider", 8));
        SaveLayout(scene, layout);
        UseSubscene(EditorSceneManager.OpenScene("Assets/Scenes/TestSwarm.unity", OpenSceneMode.Single), "TestSwarm");
        EditorSceneManager.SaveOpenScenes();
    }

    static void CopyTemplate(string name)
    {
        string path = $"Assets/Scenes/{name}.unity";
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null) return;
        if (!AssetDatabase.IsValidFolder($"Assets/Scenes/{name}")) AssetDatabase.CreateFolder("Assets/Scenes", name);
        AssetDatabase.CopyAsset(TemplateScene, path);
        AssetDatabase.CopyAsset(TemplateSubscene, $"Assets/Scenes/{name}/Entities.unity");
    }

    static void AddToBuildSettings(string path)
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(scene => scene.path == path)) return;
        scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    // The subscene with an emptied LevelLayout: a closed room, every corner inside the border walkable
    static (UnityEngine.SceneManagement.Scene, LevelLayoutAuthoring) OpenLayout(string name, int width, int height, Vector2Int start, uint seed)
    {
        var scene = EditorSceneManager.OpenScene($"Assets/Scenes/{name}/Entities.unity", OpenSceneMode.Single);
        LevelLayoutAuthoring layout = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.TryGetComponent(out LevelLayoutAuthoring found)) layout = found;
            else Object.DestroyImmediate(root);
        }

        layout.transform.position = Vector3.zero;
        layout.Kind = AreaKind.Town;
        layout.Dungeon = null;
        layout.Width = width;
        layout.Height = height;
        layout.TileSet = AssetDatabase.LoadAssetAtPath<WfcTileSet>(TileSetPath);
        layout.Placements = null;
        layout.Border = WfcCornerRule.Wall;
        layout.Mode = LevelGenerationMode.GenerateAtRuntime;
        layout.RandomSeedEachPlay = false;
        layout.Seed = seed;
        layout.WobbleStrength = 0f;
        layout.BendAngle = 0f;
        layout.HillHeight = 0f;
        layout.PlayerStart = start;
        layout.ForcedTiles.Clear();
        layout.Pieces.Clear();
        layout.EnemySpawners.Clear();
        layout.Prefabs.Clear();
        layout.ClearCornerRules();
        for (int y = 1; y < height; y++)
            for (int x = 1; x < width; x++)
                layout.SetCornerRule(x, y, WfcCornerRule.Walkable);
        return (scene, layout);
    }

    static void SaveLayout(UnityEngine.SceneManagement.Scene scene, LevelLayoutAuthoring layout)
    {
        EditorUtility.SetDirty(layout);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    static void UseSubscene(UnityEngine.SceneManagement.Scene scene, string name)
    {
        var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>($"Assets/Scenes/{name}/Entities.unity");
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == "Entities" && root.TryGetComponent(out SubScene subScene))
            {
                subScene.SceneAsset = asset;
                EditorUtility.SetDirty(subScene);
            }
        EditorSceneManager.MarkSceneDirty(scene);
    }

    static void AddPortal(UnityEngine.SceneManagement.Scene scene, string target, string displayName, float x, float z)
    {
        var portal = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PortalPrefab), scene);
        portal.name = $"Portal to {target}";
        portal.transform.position = new Vector3(x, 0f, z);
        var authoring = portal.GetComponent<PortalAuthoring>();
        authoring.Scene = AssetDatabase.LoadAssetAtPath<SceneAsset>($"Assets/Scenes/{target}.unity");
        authoring.DisplayName = displayName;
        PrefabUtility.RecordPrefabInstancePropertyModifications(authoring);
    }

    static void AddProp(UnityEngine.SceneManagement.Scene scene, string prop, float x, float z, float yaw = 0f)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PropFolder}{prop}.prefab");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        instance.transform.SetPositionAndRotation(new Vector3(x, 0f, z), Quaternion.Euler(0f, yaw, 0f));
    }

    static void AddSpawner(LevelLayoutAuthoring layout, int x, int y, float triggerRadius, float spawnRadius, params (string enemy, int count)[] group)
    {
        var entries = new List<EnemySpawner.Entry>();
        foreach (var (enemy, count) in group)
            entries.Add(new EnemySpawner.Entry
            {
                Definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>($"{EnemyFolder}{enemy}.asset"),
                Count = count,
            });
        layout.EnemySpawners.Add(new LevelLayoutAuthoring.SpawnerMarker
        {
            Cell = new Vector2Int(x, y), TriggerRadius = triggerRadius, SpawnRadius = spawnRadius, Group = entries,
        });
    }

    static void AddCandleLight(float x, float z)
    {
        var light = new GameObject("Candle Light").AddComponent<Light>();
        light.transform.position = new Vector3(x, 3.2f, z);
        light.type = LightType.Point;
        light.color = CandleColor;
        light.range = 15f;
        light.intensity = 14f;
        light.shadows = LightShadows.None;
    }
}
