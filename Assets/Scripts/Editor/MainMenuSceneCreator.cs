using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Builds the MainMenu scene (camera, AccountManager and the menu UI) and makes it the first scene in Build Settings,
/// so the game starts there. Run it again to rebuild the scene.
/// </summary>
public static class MainMenuSceneCreator
{
    const string ScenePath = "Assets/Scenes/MainMenu.unity";
    const string UxmlPath = "Assets/UI/MainMenu/MainMenu.uxml";
    const string PanelSettingsPath = "Assets/UI/PanelSettings.asset";
    const string AccountManagerPath = "Assets/Prefabs/AccountManager.prefab";

    [MenuItem("Tools/Create Main Menu Scene")]
    static void Create()
    {
        var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
        var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        var accountManager = AssetDatabase.LoadAssetAtPath<GameObject>(AccountManagerPath);
        if (uxml == null || panelSettings == null || accountManager == null)
        {
            Debug.LogError($"Create Main Menu Scene: missing {UxmlPath}, {PanelSettingsPath} or {AccountManagerPath}.");
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
            !EditorUtility.DisplayDialog("Create Main Menu Scene", $"{ScenePath} already exists. Replace it?", "Replace", "Cancel"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color32(13, 14, 18, 255);

        PrefabUtility.InstantiatePrefab(accountManager, scene);

        var menu = new GameObject("MainMenu");
        menu.AddComponent<MainMenuHUD>();       // Adds the PanelRenderer
        var renderer = new SerializedObject(menu.GetComponent<PanelRenderer>());
        var settingsProperty = renderer.FindProperty("m_PanelSettings");
        var sourceProperty = renderer.FindProperty("sourceAsset");
        if (settingsProperty != null) settingsProperty.objectReferenceValue = panelSettings;
        if (sourceProperty != null) sourceProperty.objectReferenceValue = uxml;
        renderer.ApplyModifiedPropertiesWithoutUndo();
        if (settingsProperty == null || sourceProperty == null)
            Debug.LogWarning("Create Main Menu Scene: set the MainMenu Panel Renderer's Panel Settings and Source Asset by hand " +
                             $"({PanelSettingsPath}, {UxmlPath}).", menu);

        EditorSceneManager.SaveScene(scene, ScenePath);

        // First in Build Settings: the game starts in the menu
        var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();

        Debug.Log($"Created {ScenePath} and made it the first scene in Build Settings.");
    }
}
