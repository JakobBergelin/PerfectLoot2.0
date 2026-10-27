using System.Collections.Generic;
using System.Linq;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-off: moves what SampleScene and HauntedCathedral both had into shared assets (GameBootstrap). SampleScene's
/// camera, HUDs and gameplay managers become the GameRig prefab, AccountManager and LootGenerationManager the
/// GameServices prefab, and its subscene's camera follow, GameSettings, player prefab and enemy list the CoreEntities
/// subscene, which both level scenes reference. The copies in HauntedCathedral and MainMenu are removed. Commit first:
/// git is the undo. Delete this file afterwards.
/// </summary>
public static class GameRigMigration
{
    const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
    const string SampleEntitiesPath = "Assets/Scenes/SampleScene/Entities.unity";
    const string CathedralScenePath = "Assets/Scenes/HauntedCathedral.unity";
    const string CathedralEntitiesPath = "Assets/Scenes/HauntedCathedral/Entities.unity";
    const string MainMenuPath = "Assets/Scenes/MainMenu.unity";
    const string CoreFolder = "Assets/Scenes/Core";
    const string CoreEntitiesPath = CoreFolder + "/CoreEntities.unity";
    const string RigPath = "Assets/Prefabs/GameRig.prefab";
    const string ServicesPath = "Assets/Prefabs/GameServices.prefab";
    const string BootstrapPath = "Assets/Resources/GameBootstrap.asset";
    const string PlayerPrefabAuthoringPath = "Assets/Prefabs/PlayerPrefabAuthoring.prefab";
    const string CathedralVisualsPath = "Assets/Prefabs/Cathedral/PL_Player_Visuals.prefab";

    // Root objects, by name (as in SampleScene)
    static readonly string[] ServiceNames = { "AccountManager", "LootGenerationManager" };
    static readonly string[] RigNames =
    {
        "MainCamera", "AssetStorage",
        "CharacterManager", "InventoryManager", "ActiveSkillManager", "PassiveSkillManager", "EnemyManager",
        "CharacterPanelHUD", "CombatTextHUD", "DropPopupHUD", "EffectBarHUD", "ItemDatabasePanelHUD", "PortalHUD",
        "SkillBarHUD", "SkillsPanelHUD",
    };
    static readonly string[] CoreEntityNames = { "Camera", "GameSettings", "PlayerPrefabAuthoring", "EnemyPrefabs" };
    static readonly string[] RemovedEntityNames = { "Ground" };   // The generated level brings its own ground

    [MenuItem("Tools/Migrate Scenes to Game Rig")]
    static void Migrate()
    {
        foreach (var path in new[] { RigPath, ServicesPath, CoreEntitiesPath, BootstrapPath })
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                EditorUtility.DisplayDialog("Migrate Scenes to Game Rig", $"{path} already exists, so the migration has run. Revert with git to run it again.", "OK");
                return;
            }
        foreach (var path in new[] { SampleScenePath, SampleEntitiesPath, CathedralScenePath, CathedralEntitiesPath, MainMenuPath, CathedralVisualsPath, PlayerPrefabAuthoringPath })
            if (AssetDatabase.LoadMainAssetAtPath(path) == null)
            {
                Debug.LogError($"Migrate Scenes to Game Rig: {path} is missing.");
                return;
            }
        if (!EditorUtility.DisplayDialog("Migrate Scenes to Game Rig",
                "Moves the shared camera, HUDs, managers and subscene objects into GameRig, GameServices and the CoreEntities subscene, " +
                "and removes the copies from SampleScene, HauntedCathedral and MainMenu.\n\nCommit first: git is the undo.", "Migrate", "Cancel"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var log = new List<string>();
        UseCathedralVisuals(log);
        MigrateEntities(log);
        var (rig, services) = MigrateSampleScene(log);
        MigrateLevelScene(CathedralScenePath, log);
        RemoveRoots(MainMenuPath, ServiceNames, log);
        CreateBootstrap(rig, services);
        log.Add($"{BootstrapPath}: Rig {RigPath}, Services {ServicesPath}, menu scene MainMenu.");

        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
        Debug.Log("Migrated scenes to the game rig:\n" + string.Join("\n", log));
    }

    // The player looks the same in every level (for now the Cathedral visuals): set on the prefab, not per scene
    static void UseCathedralVisuals(List<string> log)
    {
        var authoring = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabAuthoringPath).GetComponent<PlayerPrefabAuthoring>();
        authoring.PlayerVisualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CathedralVisualsPath);
        EditorUtility.SetDirty(authoring);
        log.Add($"{PlayerPrefabAuthoringPath}: Player Visual Prefab is {CathedralVisualsPath}.");
    }

    // SampleScene's shared subscene objects move to CoreEntities; HauntedCathedral's copies are removed
    static void MigrateEntities(List<string> log)
    {
        var sample = EditorSceneManager.OpenScene(SampleEntitiesPath, OpenSceneMode.Single);
        var core = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        foreach (var root in FindRoots(sample, CoreEntityNames, log)) SceneManager.MoveGameObjectToScene(root, core);
        foreach (var root in FindRoots(sample, RemovedEntityNames, log)) Object.DestroyImmediate(root);
        if (!AssetDatabase.IsValidFolder(CoreFolder)) AssetDatabase.CreateFolder("Assets/Scenes", "Core");
        EditorSceneManager.SaveScene(core, CoreEntitiesPath);
        EditorSceneManager.SaveScene(sample);
        log.Add($"{CoreEntitiesPath}: {string.Join(", ", CoreEntityNames)} from {SampleEntitiesPath} (Ground removed).");

        RemoveRoots(CathedralEntitiesPath, CoreEntityNames.Concat(RemovedEntityNames).ToArray(), log, optional: RemovedEntityNames);
    }

    // SampleScene's camera, HUDs and managers become the two prefabs (nested, so their overrides come along)
    static (GameObject Rig, GameObject Services) MigrateSampleScene(List<string> log)
    {
        var scene = EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
        var rig = SaveAsPrefab(scene, "GameRig", RigNames, RigPath, log);
        var services = SaveAsPrefab(scene, "GameServices", ServiceNames, ServicesPath, log);
        AddCoreSubScene(scene);
        EditorSceneManager.SaveScene(scene);
        log.Add($"{SampleScenePath}: CoreEntities subscene added.");
        return (rig, services);
    }

    static void MigrateLevelScene(string path, List<string> log)
    {
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        foreach (var root in FindRoots(scene, RigNames.Concat(ServiceNames).ToArray(), log, optional: new[] { "PortalHUD" }))
            Object.DestroyImmediate(root);
        AddCoreSubScene(scene);
        EditorSceneManager.SaveScene(scene);
        log.Add($"{path}: rig and services copies removed, CoreEntities subscene added.");
    }

    static GameObject SaveAsPrefab(Scene scene, string name, string[] names, string path, List<string> log)
    {
        var root = new GameObject(name);
        SceneManager.MoveGameObjectToScene(root, scene);
        foreach (var child in FindRoots(scene, names, log)) child.transform.SetParent(root.transform, true);
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        log.Add($"{path}: {string.Join(", ", names)}.");
        return prefab;
    }

    static void AddCoreSubScene(Scene scene)
    {
        var holder = new GameObject("CoreEntities");
        SceneManager.MoveGameObjectToScene(holder, scene);
        var subScene = holder.AddComponent<SubScene>();
        subScene.AutoLoadScene = true;
        subScene.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(CoreEntitiesPath);
    }

    static void RemoveRoots(string path, string[] names, List<string> log, string[] optional = null)
    {
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        foreach (var root in FindRoots(scene, names, log, optional)) Object.DestroyImmediate(root);
        EditorSceneManager.SaveScene(scene);
        log.Add($"{path}: {string.Join(", ", names)} removed.");
    }

    static List<GameObject> FindRoots(Scene scene, string[] names, List<string> log, string[] optional = null)
    {
        var roots = scene.GetRootGameObjects();
        var found = new List<GameObject>();
        foreach (var name in names)
        {
            var root = roots.FirstOrDefault(r => r.name == name);
            if (root != null) found.Add(root);
            else if (optional == null || !optional.Contains(name)) log.Add($"  (warning) {scene.path} has no root object named {name}.");
        }
        return found;
    }

    static void CreateBootstrap(GameObject rig, GameObject services)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        var bootstrap = ScriptableObject.CreateInstance<GameBootstrap>();
        bootstrap.Rig = rig;
        bootstrap.Services = services;
        AssetDatabase.CreateAsset(bootstrap, BootstrapPath);
    }
}
