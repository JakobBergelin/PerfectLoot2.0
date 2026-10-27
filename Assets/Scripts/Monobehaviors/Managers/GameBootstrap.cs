using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Creates what every scene shares, so scenes only hold their own content (lighting and their level subscene).
/// Services (account, loot) are made before the first scene and live all session. The Rig (camera, HUDs, gameplay
/// managers) is made when a game scene loads, kept through portals, and removed in menu scenes, so a character picked
/// in Character Select starts with a fresh rig. Settings: Resources/GameBootstrap. Pressing Play in any scene works.
/// </summary>
[CreateAssetMenu(fileName = "GameBootstrap", menuName = "Game/Bootstrap")]
public class GameBootstrap : ScriptableObject
{
    const string ResourcePath = "GameBootstrap";

    [Tooltip("Account and loot: made before the first scene, kept all session")]
    public GameObject Services;
    [Tooltip("Camera, HUDs and gameplay managers: made in game scenes, kept between them")]
    public GameObject Rig;
    [Tooltip("Scenes without the rig")]
    public List<string> MenuScenes = new List<string> { "MainMenu" };

    static GameBootstrap _settings;
    static GameObject _rig;

    // Statics survive Play mode when domain reload is off
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        _settings = null;
        _rig = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Initialize()
    {
        _settings = Resources.Load<GameBootstrap>(ResourcePath);
        if (_settings == null)
        {
            Debug.LogWarning($"GameBootstrap: no Resources/{ResourcePath} asset, so scenes must bring their own managers, camera and HUDs.");
            return;
        }
        if (_settings.Services != null) Spawn(_settings.Services);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // After the scene's Awake, before its Start
    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        bool menu = _settings.MenuScenes.Contains(scene.name);
        if (menu && _rig != null)
        {
            Destroy(_rig);
            _rig = null;
        }
        else if (!menu && _rig == null && _settings.Rig != null)
        {
            _rig = Spawn(_settings.Rig);
        }
    }

    static GameObject Spawn(GameObject prefab)
    {
        var instance = Instantiate(prefab);
        instance.name = prefab.name;
        DontDestroyOnLoad(instance);
        return instance;
    }
}
