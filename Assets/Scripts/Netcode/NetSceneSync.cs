using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

/// <summary>The host tells its clients to load a scene, with the seed for its level (LevelSeed).</summary>
public struct LoadSceneRequest : IRpcCommand
{
    public FixedString64Bytes Scene;
    public uint Seed;
}

/// <summary>
/// Scene changes in a networked game. GameSceneLoader rebuilds the world on every scene change; this keeps the network
/// connections through that and keeps the players together:
/// - The host first sends LoadSceneRequest (scene and level seed) to its clients and loads a few frames later, so the
///   message goes out before its world is disposed. Alone, it loads at once, as before.
/// - A client loads the scene when the request arrives; it doesn't change scenes on its own (only back to the main menu).
/// - Before a world is disposed its connections move to a backup world through Netcode's DriverMigrationSystem, kept in
///   a small world of its own that outlives the game world; NetBootstrap builds the new world around them. Remote
///   players leave the game until their client has loaded and sent its JoinRequest again (NetJoinSystem).
/// Going back to the main menu drops the connections instead.
/// </summary>
public static class NetSceneSync
{
    const int HostLoadDelayFrames = 3;

    static World _migrationWorld;
    static DriverMigrationSystem _migration;
    static int _pendingTicket;
    static string _followScene;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _migrationWorld = null;
        _migration = null;
        _pendingTicket = 0;
        _followScene = null;
    }

    /// <summary>The host: whether any remote client is connected (then scene changes are sent and delayed).</summary>
    public static bool HasRemoteClients(World world)
    {
        if (world == null || !world.IsCreated) return false;
        using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>());
        return !query.IsEmpty;
    }

    /// <summary>The host: tells every connected client to load the scene with this seed.</summary>
    public static void SendLoad(World world, string scene, uint seed)
    {
        var entityManager = world.EntityManager;
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>());
        using var connections = query.ToEntityArray(Allocator.Temp);
        foreach (var connection in connections)
        {
            var rpc = entityManager.CreateEntity();
            entityManager.AddComponentData(rpc, new LoadSceneRequest { Scene = scene, Seed = seed });
            entityManager.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
        }
    }

    /// <summary>The host: loads the scene a few frames from now, after its LoadSceneRequest has gone out.</summary>
    public static void LoadLater(string scene) => Runner.Get().LoadAfter(scene, HostLoadDelayFrames);

    /// <summary>A client: the host asked for this scene (the next GameSceneLoader.Load of it is allowed).</summary>
    public static void Follow(string scene, uint seed)
    {
        _followScene = scene;
        LevelSeed.Next = seed;
        Runner.Get().LoadAfter(scene, 0);
    }

    /// <summary>A client may load this scene now: one the host asked for, or the main menu (leaving).</summary>
    public static bool ClientMayLoad(string scene)
    {
        if (scene == GameBootstrap.MainMenuScene) return true;
        if (scene != _followScene) return false;
        _followScene = null;
        return true;
    }

    /// <summary>
    /// Before the world is disposed: its connections move to a backup world, out of the game for now. Not when leaving
    /// for the main menu (the session ends).
    /// </summary>
    public static void StoreConnections(World world, bool keep)
    {
        _pendingTicket = 0;
        if (!keep || world == null || !world.IsCreated) return;
        var entityManager = world.EntityManager;
        using (var driver = entityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamDriver>()))
            if (driver.IsEmpty) return;
        using (var connections = entityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>()))
            entityManager.RemoveComponent<NetworkStreamInGame>(connections);
        _pendingTicket = Migration().StoreWorld(world);
    }

    /// <summary>For NetBootstrap: the world to build the new game world in (with the stored connections), or null for a fresh one.</summary>
    public static World TakeMigratedWorld()
    {
        if (_pendingTicket == 0) return null;
        var world = Migration().LoadWorld(_pendingTicket);
        _pendingTicket = 0;
        return world;
    }

    static DriverMigrationSystem Migration()
    {
        if (_migration != null && _migrationWorld != null && _migrationWorld.IsCreated) return _migration;
        _migrationWorld = new World("NetMigration");
        _migration = _migrationWorld.GetOrCreateSystemManaged<DriverMigrationSystem>();
        return _migration;
    }

    /// <summary>Runs loads on a later frame (not from inside a world's update, which may dispose that world).</summary>
    class Runner : MonoBehaviour
    {
        static Runner _instance;
        string _scene;
        int _frames = -1;

        public static Runner Get()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("NetSceneSync");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<Runner>();
            return _instance;
        }

        public void LoadAfter(string scene, int frames)
        {
            _scene = scene;
            _frames = frames;
        }

        void Update()
        {
            if (_frames < 0) return;
            if (_frames-- > 0) return;
            var scene = _scene;
            _scene = null;
            GameSceneLoader.LoadNow(scene);
        }
    }
}

/// <summary>On a client: loads the scene the host asked for (NetSceneSync.Follow).</summary>
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct LoadSceneReceiveSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        // A host never follows (its own client half, or a test client world inside a host)
        if (NetSession.IsHost) return;
        var commandBuffer = new EntityCommandBuffer(Allocator.Temp);
        foreach (var (request, rpc) in SystemAPI.Query<RefRO<LoadSceneRequest>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
        {
            NetSceneSync.Follow(request.ValueRO.Scene.ToString(), request.ValueRO.Seed);
            commandBuffer.DestroyEntity(rpc);
        }
        commandBuffer.Playback(state.EntityManager);
    }
}
