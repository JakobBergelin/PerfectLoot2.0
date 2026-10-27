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
    /// <summary>Resets the ECS world and loads the scene (next frame). False when the scene isn't in Build Settings.</summary>
    public static bool Load(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"GameSceneLoader: scene '{sceneName}' isn't in Build Settings.");
            return false;
        }

        ResetWorld();
        SceneManager.LoadScene(sceneName);
        return true;
    }

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
