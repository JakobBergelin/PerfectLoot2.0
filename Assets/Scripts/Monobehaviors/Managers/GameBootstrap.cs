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
    [Tooltip("Editor: pressing Play in a game scene plays the Dev account's last played character (else its first), as if " +
             "picked in Character Select. Off: the rig's own Class, Resource and slots, not saved")]
    public bool SelectDevCharacter = true;

    static GameBootstrap _settings;
    static GameObject _rig;
    static bool _sceneLoaded;

    // Statics survive Play mode when domain reload is off
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        _settings = null;
        _rig = null;
        _sceneLoaded = false;
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

    // Play pressed in a scene with unsaved changes plays it from its backup, which doesn't raise sceneLoaded: the first
    // scene is handled here instead (still before its Start)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AfterFirstSceneLoad()
    {
        if (_settings == null || _sceneLoaded) return;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    // After the scene's Awake, before its Start
    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        bool menu = _settings.MenuScenes.Contains(scene.name);
        bool firstScene = !_sceneLoaded;
        _sceneLoaded = true;
        if (menu && _rig != null)
        {
            Destroy(_rig);
            _rig = null;
        }
        else if (!menu && _rig == null && _settings.Rig != null)
        {
#if UNITY_EDITOR
            // Before the rig, so its managers read the character in Start like after Character Select
            if (firstScene && _settings.SelectDevCharacter) SelectDevAccountCharacter();
#endif
            _rig = Spawn(_settings.Rig);
        }
    }

#if UNITY_EDITOR
    // Play pressed in a game scene: the account is still the default (Dev) one, nothing was picked
    static void SelectDevAccountCharacter()
    {
        var account = AccountManager.Instance;
        if (account == null || account.SelectedCharacter != null) return;
        var character = account.LastPlayedCharacter ?? (account.Characters.Count > 0 ? account.Characters[0] : null);
        if (character == null)
        {
            Debug.Log($"GameBootstrap: the {account.AccountName} account has no characters (create one in the main menu), " +
                      "so the rig's own Class, Resource and slots are used.");
            return;
        }
        account.SelectCharacter(character.Guid);
        Debug.Log($"GameBootstrap: playing {account.AccountName}'s {character.Name} (level {character.Level} {character.Class}), " +
                  "as if picked in Character Select. Turn off Select Dev Character in Resources/GameBootstrap to use the rig's setup.");
    }
#endif

    static GameObject Spawn(GameObject prefab)
    {
        var instance = Instantiate(prefab);
        instance.name = prefab.name;
        DontDestroyOnLoad(instance);
        return instance;
    }
}
