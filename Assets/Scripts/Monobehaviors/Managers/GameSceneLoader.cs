using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Loads a game scene with a fresh ECS world. Unloading a scene only removes its subscene's entities; everything made
/// while playing (player, enemies, projectiles, the generated level) would stay in the world. So the world is replaced;
/// the managers kept between scenes (GameBootstrap's rig) notice the new world and spawn the character in it.
/// </summary>
public static class GameSceneLoader
{
    /// <summary>The scene played before the current one (where a dungeon's return portal leads), or null.</summary>
    public static string PreviousScene { get; private set; }

    /// <summary>Raised just before the world is replaced, while the old scene's entities still exist (CharacterManager saves progress).</summary>
    public static event System.Action Loading;

    /// <summary>Resets the ECS world and loads the scene (next frame). False when the scene isn't in Build Settings.</summary>
    public static bool Load(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"GameSceneLoader: scene '{sceneName}' isn't in Build Settings.");
            return false;
        }

        PreviousScene = SceneManager.GetActiveScene().name;
        Loading?.Invoke();
        ResetWorld();
        SceneManager.LoadScene(sceneName);
        return true;
    }

    // Statics survive entering Play mode when domain reload is off
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => PreviousScene = null;

    // Systems of the disposed world that are still in this frame's player loop skip their update (Entities checks)
    static void ResetWorld()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world != null && world.IsCreated)
        {
            ScriptBehaviourUpdateOrder.RemoveWorldFromCurrentPlayerLoop(world);
            world.Dispose();
        }
        DefaultWorldInitialization.Initialize("Default World");
    }
}
