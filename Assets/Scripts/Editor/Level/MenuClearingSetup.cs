using LevelGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the clearing behind the main menu: a small level from Brightwater Hollow's biome in its own subscene
/// (Scenes/MainMenu/Entities, a copy of Brightwater Hollow's with the layout repainted), solved with a new seed every
/// time the menu loads. An open stage for the characters (the player start); everything around it, forest and water, is
/// left to the solver, so some clearings have water and some don't. Also gives the main menu scene Brightwater Hollow's light and volume, and its camera the game camera's rendering plus MenuSceneCamera and MenuCharacters (with the core subscene, for the player's model). Safe to run again.
/// </summary>
public static class MenuClearingSetup
{
    const string MenuScene = "Assets/Scenes/MainMenu.unity";
    const string Subscene = "Assets/Scenes/MainMenu/Entities.unity";
    const string SourceScene = "Assets/Scenes/BrightwaterHollow.unity";
    const string SkyMaterial = "Assets/ArtStyleTest/Materials/PL_CloudSky.mat";
    const string CoreSubscene = "Assets/Scenes/Core/CoreEntities.unity";
    const string SourceSubscene = "Assets/Scenes/BrightwaterHollow/Entities.unity";

    public const int Width = 18, Height = 14;
    static readonly Vector2Int Start = new Vector2Int(9, 3);

    // The biome kit's corner connections (pl_wfc_kit CONNECTIONS)
    const int Floor = 0;

    [MenuItem("Tools/Level/Build Main Menu Clearing")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Subscene) == null)
        {
            AssetDatabase.CreateFolder("Assets/Scenes", "MainMenu");
            AssetDatabase.CopyAsset(SourceSubscene, Subscene);
        }
        MakeSky();
        PaintLevel();
        SetUpMenuScene();
        Debug.Log($"Main menu clearing built: {Width}x{Height} cells, subscene {Subscene}.");
    }

    static void PaintLevel()
    {
        var scene = EditorSceneManager.OpenScene(Subscene, OpenSceneMode.Single);
        LevelLayoutAuthoring layout = null;
        foreach (var root in scene.GetRootGameObjects())
            if (layout == null) layout = root.GetComponentInChildren<LevelLayoutAuthoring>(true);
        layout.Kind = AreaKind.Town;    // No dungeon: nothing to run, nothing to leave
        layout.Dungeon = null;
        layout.Width = Width;
        layout.Height = Height;
        layout.PlayerStart = Start;
        layout.Mode = LevelGenerationMode.GenerateAtRuntime;
        layout.RandomSeedEachPlay = true;
        layout.LoadDistance = 200f;     // All of it at once
        layout.Placements = null;
        layout.ForcedTiles.Clear();
        layout.Pieces.Clear();
        layout.EnemySpawners.Clear();
        layout.Prefabs.Clear();
        layout.Landmarks.Clear();
        layout.ClearCornerRules();

        // The stage: open floor around the player start, where the characters stand
        for (int y = Start.y - 1; y <= Start.y + 1; y++)
            for (int x = Start.x - 1; x <= Start.x + 2; x++)
                layout.SetCornerRule(x, y, WfcCornerRule.Walkable, Floor);

        EditorUtility.SetDirty(layout);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    // The comic cloud sky (PerfectLoot/CloudSky) in Brightwater Hollow's colours
    static void MakeSky()
    {
        if (AssetDatabase.LoadAssetAtPath<Material>(SkyMaterial) != null) return;
        var sky = new Material(Shader.Find("PerfectLoot/CloudSky"));
        AssetDatabase.CreateAsset(sky, SkyMaterial);
    }

    // Brightwater Hollow's light and volume, the subscene, and a camera that renders like the game's
    static void SetUpMenuScene()
    {
        var menu = EditorSceneManager.OpenScene(MenuScene, OpenSceneMode.Single);
        var source = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Additive);
        foreach (var name in new[] { "Directional Light", "Global Volume" })
        {
            if (Find(menu, name) != null) continue;
            var original = Find(source, name);
            if (original == null) continue;
            var copy = Object.Instantiate(original);
            copy.name = name;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(copy, menu);
        }
        // Its fog, ambient light and sky (RenderSettings belong to the active scene)
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(source);
        bool fog = RenderSettings.fog;
        var fogColor = RenderSettings.fogColor;
        var fogMode = RenderSettings.fogMode;
        float fogDensity = RenderSettings.fogDensity, fogStart = RenderSettings.fogStartDistance, fogEnd = RenderSettings.fogEndDistance;
        var ambientMode = RenderSettings.ambientMode;
        Color sky = RenderSettings.ambientSkyColor, equator = RenderSettings.ambientEquatorColor, ground = RenderSettings.ambientGroundColor;

        UnityEngine.SceneManagement.SceneManager.SetActiveScene(menu);
        RenderSettings.fog = fog;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogMode = fogMode;
        RenderSettings.fogDensity = fogDensity;
        RenderSettings.fogStartDistance = fogStart;
        RenderSettings.fogEndDistance = fogEnd;
        RenderSettings.ambientMode = ambientMode;
        RenderSettings.ambientSkyColor = sky;
        RenderSettings.ambientEquatorColor = equator;
        RenderSettings.ambientGroundColor = ground;
        RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterial);   // Clouds: this view sees the horizon
        EditorSceneManager.CloseScene(source, true);

        if (Find(menu, "Entities") == null)
        {
            var holder = new GameObject("Entities");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, menu);
            var subscene = holder.AddComponent<Unity.Scenes.SubScene>();
            subscene.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(Subscene);
            subscene.AutoLoadScene = true;
        }
        // The core subscene: the player's model (PlayerPrefab) and its equipment looks, for MenuCharacters
        if (Find(menu, "CoreEntities") == null)
        {
            var holder = new GameObject("CoreEntities");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, menu);
            var subscene = holder.AddComponent<Unity.Scenes.SubScene>();
            subscene.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(CoreSubscene);
            subscene.AutoLoadScene = true;
        }

        var cameraObject = Find(menu, "Main Camera");
        var camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.nearClipPlane = 3f;       // A leaf may blow past the characters, but none right at the lens
        camera.fieldOfView = 40f;
        var data = cameraObject.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var menuCamera = cameraObject.GetComponent<MenuSceneCamera>();
        if (menuCamera == null) menuCamera = cameraObject.AddComponent<MenuSceneCamera>();
        if (cameraObject.GetComponent<MenuCharacters>() == null) cameraObject.AddComponent<MenuCharacters>();
        menuCamera.Distance = 5.5f;
        menuCamera.Height = 1.9f;
        menuCamera.LookHeight = 1.25f;

        EditorSceneManager.MarkSceneDirty(menu);
        EditorSceneManager.SaveScene(menu);
    }

    static GameObject Find(UnityEngine.SceneManagement.Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == name) return root;
        return null;
    }
}
