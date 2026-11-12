using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using UnityEngine.SceneManagement;

/// <summary>A client asks the host to join with this many local (couch) players, from this scene.</summary>
public struct JoinRequest : IRpcCommand
{
    public int LocalPlayers;
    public FixedString64Bytes Scene;
}

public enum JoinRefusal : byte { Full, NotInTown }

/// <summary>The host to a client: it can't join now (NetSession.Leave takes it back to its own game).</summary>
public struct JoinRefused : IRpcCommand
{
    public JoinRefusal Reason;
}

/// <summary>On the host, on a remote client's connection: how many players it brought (NetPlayerSpawner makes their ghosts).</summary>
public struct JoinedPlayers : IComponentData
{
    public int Count;
}

/// <summary>
/// Starts the network for this world: a host listens while NetSession.Listen is on, a client connects to
/// NetSession.ConnectTo. Once, when the world is made; not when the world was rebuilt around connections kept through a
/// scene change (NetSceneSync).
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
public partial struct NetStartSystem : ISystem
{
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<NetworkStreamDriver>();

    public void OnUpdate(ref SystemState state)
    {
        state.Enabled = false;
        ref var driver = ref SystemAPI.GetSingletonRW<NetworkStreamDriver>().ValueRW;
        // A world rebuilt around connections kept through a scene change is already listening or connected
        if (state.WorldUnmanaged.IsServer())
        {
            var store = driver.DriverStore;
            bool listening = store.DriversCount > 0 && store.GetDriverRO(store.FirstDriver).Listening;
            if (NetSession.Listen && !listening) driver.Listen(NetworkEndpoint.AnyIpv4.WithPort(NetSession.Port));
        }
        else if (SystemAPI.QueryBuilder().WithAll<NetworkStreamConnection>().Build().IsEmpty)
            driver.Connect(state.EntityManager, NetSession.ConnectTo);
    }
}

/// <summary>
/// Joining. A client, once connected and its scene's ghost prefabs are loaded, goes in game and sends a JoinRequest with
/// its local player count and scene (again after every scene change). The host takes its own local connection in game at
/// once. A remote one that is new to the game is turned down (JoinRefused) when it would make more than
/// NetSession.MaxPlayers, or when the host isn't in town; one in another scene is sent the host's (LoadSceneRequest) and
/// asks again from there. Otherwise it goes in game. Netcode only runs its prediction loop, and with it physics, once a
/// connection is in game.
/// A client that is turned down, can't reach the host or loses it goes back to its own game (NetSession.Leave).
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
public partial struct NetJoinSystem : ISystem
{
    // A client: its connection has had its NetworkId (a world rebuilt around a kept connection has it from the start)
    bool _connected;

    public void OnUpdate(ref SystemState state)
    {
        var commandBuffer = new EntityCommandBuffer(Allocator.Temp);
        if (state.WorldUnmanaged.IsServer()) UpdateHost(ref state, commandBuffer);
        else UpdateClient(ref state, commandBuffer);
        commandBuffer.Playback(state.EntityManager);
    }

    void UpdateHost(ref SystemState state, EntityCommandBuffer commandBuffer)
    {
        // The host's own players
        foreach (var (_, connection) in SystemAPI.Query<RefRO<NetworkId>>().WithAll<LocalConnection>().WithNone<NetworkStreamInGame>().WithEntityAccess())
            commandBuffer.AddComponent<NetworkStreamInGame>(connection);

        int remotePlayers = 0;
        foreach (var joined in SystemAPI.Query<RefRO<JoinedPlayers>>()) remotePlayers += joined.ValueRO.Count;
        NetSession.RemotePlayers = remotePlayers;

        string hostScene = SceneManager.GetActiveScene().name;
        foreach (var (request, receive, rpc) in SystemAPI.Query<RefRO<JoinRequest>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
        {
            commandBuffer.DestroyEntity(rpc);
            var connection = receive.ValueRO.SourceConnection;
            if (!state.EntityManager.Exists(connection)) continue;
            int players = math.max(1, request.ValueRO.LocalPlayers);

            // New to the game (not back after a scene change): only into a town, and only while there's room
            bool member = SystemAPI.HasComponent<JoinedPlayers>(connection);
            if (!member)
            {
                if (GameInput.PlayerCount + remotePlayers + players > NetSession.MaxPlayers) { Refuse(commandBuffer, connection, JoinRefusal.Full); continue; }
                // The main menu counts as town for GameArea, but there's no game to join
                if (!GameArea.IsTown || GameBootstrap.IsMenuScene(hostScene)) { Refuse(commandBuffer, connection, JoinRefusal.NotInTown); continue; }
            }
            // Somewhere else: to the host's scene first, which asks again from there
            if (request.ValueRO.Scene.ToString() != hostScene)
            {
                Send(commandBuffer, connection, new LoadSceneRequest { Scene = hostScene, Seed = LevelSeed.Current });
                continue;
            }

            // The client's world is new (after a scene change too): it has no ghost prefabs until it says so, so the
            // host sends it the prefab list before any ghosts
            var ack = SystemAPI.GetComponentRW<NetworkSnapshotAck>(connection);
            ack.ValueRW.NumLoadedPrefabs = 0;
            commandBuffer.AddComponent<NetworkStreamInGame>(connection);
            commandBuffer.AddComponent(connection, new JoinedPlayers { Count = players });
            if (!member) remotePlayers += players;
        }
    }

    static void Refuse(EntityCommandBuffer commandBuffer, Entity connection, JoinRefusal reason) =>
        Send(commandBuffer, connection, new JoinRefused { Reason = reason });

    static void Send<T>(EntityCommandBuffer commandBuffer, Entity connection, T rpc) where T : unmanaged, IRpcCommand
    {
        var entity = commandBuffer.CreateEntity();
        commandBuffer.AddComponent(entity, rpc);
        commandBuffer.AddComponent(entity, new SendRpcCommandRequest { TargetConnection = connection });
    }

    void UpdateClient(ref SystemState state, EntityCommandBuffer commandBuffer)
    {
        // A host's own client half never leaves
        if (NetSession.IsHost) return;

        foreach (var (refused, rpc) in SystemAPI.Query<RefRO<JoinRefused>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
        {
            commandBuffer.DestroyEntity(rpc);
            NetSession.Leave(refused.ValueRO.Reason == JoinRefusal.Full
                ? $"That game is full ({NetSession.MaxPlayers} players at most)."
                : "The host isn't in town. Join when they're back in the Hub.");
            return;
        }

        if (!SystemAPI.QueryBuilder().WithAll<NetworkId>().Build().IsEmpty) _connected = true;
        foreach (var connectionEvent in SystemAPI.GetSingleton<NetworkStreamDriver>().ConnectionEventsForTick)
        {
            if (connectionEvent.State != ConnectionState.State.Disconnected) continue;
            NetSession.Leave(!_connected ? "Couldn't reach a game at that address."
                : connectionEvent.DisconnectReason == NetworkStreamDisconnectReason.ClosedByRemote ? "The host left the game."
                : "The connection to the host was lost.");
            return;
        }

        // Only once every subscene of the scene has loaded (after a scene change too): the ghost prefabs, so the host's
        // snapshots can be read, and the level, so the players have ground to stand on
        if (!SystemAPI.HasSingleton<PlayerPrefab>()) return;
        foreach (var (_, scene) in SystemAPI.Query<RefRO<SceneReference>>().WithEntityAccess())
            if (!Unity.Scenes.SceneSystem.IsSceneLoaded(state.WorldUnmanaged, scene)) return;
        foreach (var (_, connection) in SystemAPI.Query<RefRO<NetworkId>>().WithNone<NetworkStreamInGame>().WithEntityAccess())
        {
            commandBuffer.AddComponent<NetworkStreamInGame>(connection);
            Send(commandBuffer, connection, new JoinRequest
            {
                LocalPlayers = math.max(1, GameInput.PlayerCount),
                Scene = SceneManager.GetActiveScene().name,
            });
        }
    }
}

/// <summary>
/// The client side (a remote client, or the host's own players) always runs the prediction loop, so physics runs even
/// before any predicted ghost exists (with NetCodePhysicsConfig's AlwaysRun in CoreEntities).
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct NetClientConfigSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        state.Enabled = false;
        if (SystemAPI.HasSingleton<ClientTickRate>()) return;
        var tickRate = NetworkTimeSystem.DefaultClientTickRate;
        tickRate.PredictionLoopUpdateMode = PredictionLoopUpdateMode.AlwaysRun;
        state.EntityManager.CreateSingleton(tickRate);
    }
}
