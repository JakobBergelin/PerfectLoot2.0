using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;

/// <summary>A client asks the host to join with this many local (couch) players.</summary>
public struct JoinRequest : IRpcCommand
{
    public int LocalPlayers;
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
/// its local player count (again after every scene change). The host takes its own local connection in game at once,
/// and a remote one when its JoinRequest arrives (where the host turns down players beyond the limit later). Netcode only runs its prediction loop, and with it physics, once a connection is in game.
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
public partial struct NetJoinSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var commandBuffer = new EntityCommandBuffer(Allocator.Temp);
        bool isServer = state.WorldUnmanaged.IsServer();

        if (isServer)
        {
            // The host's own players
            foreach (var (_, connection) in SystemAPI.Query<RefRO<NetworkId>>().WithAll<LocalConnection>().WithNone<NetworkStreamInGame>().WithEntityAccess())
                commandBuffer.AddComponent<NetworkStreamInGame>(connection);

            foreach (var (request, receive, rpc) in SystemAPI.Query<RefRO<JoinRequest>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                var connection = receive.ValueRO.SourceConnection;
                // The client's world is new (after a scene change too): it has no ghost prefabs until it says so, so the
                // host sends it the prefab list before any ghosts
                var ack = SystemAPI.GetComponentRW<NetworkSnapshotAck>(connection);
                ack.ValueRW.NumLoadedPrefabs = 0;
                commandBuffer.AddComponent<NetworkStreamInGame>(connection);
                commandBuffer.AddComponent(connection, new JoinedPlayers { Count = request.ValueRO.LocalPlayers });
                commandBuffer.DestroyEntity(rpc);
            }
        }
        else
        {
            // Only once every subscene of the scene has loaded (after a scene change too): the ghost prefabs, so the host's
            // snapshots can be read, and the level, so the players have ground to stand on
            if (!SystemAPI.HasSingleton<PlayerPrefab>()) return;
            foreach (var (_, scene) in SystemAPI.Query<RefRO<SceneReference>>().WithEntityAccess())
                if (!Unity.Scenes.SceneSystem.IsSceneLoaded(state.WorldUnmanaged, scene)) return;
            foreach (var (_, connection) in SystemAPI.Query<RefRO<NetworkId>>().WithNone<NetworkStreamInGame>().WithEntityAccess())
            {
                commandBuffer.AddComponent<NetworkStreamInGame>(connection);
                var rpc = commandBuffer.CreateEntity();
                commandBuffer.AddComponent(rpc, new JoinRequest { LocalPlayers = math.max(1, GameInput.PlayerCount) });
                commandBuffer.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = connection });
            }
        }
        commandBuffer.Playback(state.EntityManager);
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
