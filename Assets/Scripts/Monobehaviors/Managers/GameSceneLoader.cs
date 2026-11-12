using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Loads a game scene with a fresh ECS world. Unloading a scene only removes its subscene's entities; everything made
/// while playing (player, enemies, projectiles, the generated level) would stay in the world. So the world is replaced;
/// the managers kept between scenes (GameBootstrap's rig) notice the new world and spawn the character in it.
/// In a networked game the host takes its clients along and the connections survive the new world (NetSceneSync).
/// </summary>
public static class GameSceneLoader
{
    /// <summary>The scene played before the current one (where a dungeon's return portal leads), or null.</summary>
    public static string PreviousScene { get; private set; }

    /// <summary>Raised just before the world is replaced, while the old scene's entities still exist (CharacterManager saves progress).</summary>
    public static event System.Action Loading;

    /// <summary>
    /// Resets the ECS world and loads the scene (next frame). False when the scene isn't in Build Settings, or on a
    /// client for anything but the main menu (the host changes scenes for everyone). A host with clients sends them the
    /// scene and its level seed first, and loads a few frames later.
    /// </summary>
    public static bool Load(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"GameSceneLoader: scene '{sceneName}' isn't in Build Settings.");
            return false;
        }
        if (!NetSession.IsHost)
        {
            if (!NetSceneSync.ClientMayLoad(sceneName)) return false;
            LoadNow(sceneName);
            return true;
        }

        var world = World.DefaultGameObjectInjectionWorld;
        if (sceneName != GameBootstrap.MainMenuScene && NetSceneSync.HasRemoteClients(world))
        {
            uint seed = (uint)System.DateTime.Now.Ticks;
            LevelSeed.Next = seed;
            NetSceneSync.SendLoad(world, sceneName, seed);
            NetSceneSync.LoadLater(sceneName);
            return true;
        }
        LoadNow(sceneName);
        return true;
    }

    /// <summary>Loads at once, without telling anyone (Load, or NetSceneSync when the time has come).</summary>
    public static void LoadNow(string sceneName)
    {
        bool mainMenu = sceneName == GameBootstrap.MainMenuScene;
        // The main menu ends a session in someone else's game: the next game is this machine's own
        if (mainMenu) NetSession.End();
        LoadFresh(sceneName, keepConnections: !mainMenu);
    }

    /// <summary>
    /// Starts over in this scene with a fresh world and no connections, for NetSession.Role as it is now (joining
    /// someone's game, or back to this machine's own).
    /// </summary>
    public static void Restart(string sceneName) => LoadFresh(sceneName, keepConnections: false);

    static void LoadFresh(string sceneName, bool keepConnections)
    {
        PreviousScene = SceneManager.GetActiveScene().name;
        Loading?.Invoke();
        ResetWorld(keepConnections);
        SceneManager.LoadScene(sceneName);
    }

    // Statics survive entering Play mode when domain reload is off
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => PreviousScene = null;

    // Systems of the disposed world that are still in this frame's player loop skip their update (Entities checks)
    static void ResetWorld(bool keepConnections)
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world != null && world.IsCreated)
        {
            NetSceneSync.StoreConnections(world, keepConnections);
            ScriptBehaviourUpdateOrder.RemoveWorldFromCurrentPlayerLoop(world);
            world.Dispose();
        }
        DefaultWorldInitialization.Initialize("Default World");
    }
}
