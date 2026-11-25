using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using UnityEngine.SceneManagement;

/// <summary>
/// A machine's local (couch) players: how many take a place in the game, and the couch player's character
/// (CharacterManager.Generation; 0 while there's none or they're still picking, so they have no player in the level).
/// </summary>
public struct LocalPlayers
{
    public int Count;
    public int CouchGeneration;

    /// <summary>This machine's, now.</summary>
    public static LocalPlayers Mine
    {
        get
        {
            var couch = GameInput.PlayerCount > 1 ? CharacterManager.For(1) : null;
            return new LocalPlayers { Count = math.max(1, GameInput.PlayerCount), CouchGeneration = couch != null ? couch.Generation : 0 };
        }
    }

    public bool Same(in LocalPlayers other) => Count == other.Count && CouchGeneration == other.CouchGeneration;

    /// <summary>Whether this local player's character plays in the level, and as which Generation.</summary>
    public bool Plays(int localIndex, out int generation)
    {
        generation = localIndex == 0 ? 0 : CouchGeneration;
        return localIndex == 0 || (localIndex < Count && CouchGeneration != 0);
    }
}

/// <summary>A client asks the host to join with these local players, from this scene.</summary>
public struct JoinRequest : IRpcCommand
{
    public LocalPlayers Players;
    public FixedString64Bytes Scene;
    public ulong PlatformId;            // The client's Steam account (0: none), for the leaderboards
    public FixedString32Bytes Name;     // Leaderboards.MachineName
}

/// <summary>A client in the game to the host: its local players changed (a couch player joined, left or picked a character).</summary>
public struct LocalPlayersRequest : IRpcCommand
{
    public LocalPlayers Players;
}

/// <summary>The host to a client: there's no place for its couch player (the machine itself stays in the game).</summary>
public struct CouchRefused : IRpcCommand { }

public enum JoinRefusal : byte { Full, NotInTown }

/// <summary>The host to a client: it can't join now (NetSession.Leave takes it back to its own game).</summary>
public struct JoinRefused : IRpcCommand
{
    public JoinRefusal Reason;
}

/// <summary>On the host, on a remote client's connection: the players it has (NetPlayerSpawnSystem makes their ghosts).</summary>
public struct JoinedPlayers : IComponentData
{
    public LocalPlayers Players;
    public ulong PlatformId;            // From its JoinRequest
    public FixedString32Bytes Name;
}

/// <summary>
/// Starts the network for this world: a host listens while NetSession.Listen is on or the platform hosts online (Steam),
/// on every driver it has (NetDriverConstructor); a client connects to
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
            bool listen = NetSession.Listen || Platform.Current.HostsOnline;
            if (listen && !listening) driver.Listen(NetworkEndpoint.AnyIpv4.WithPort(NetSession.Port));
        }
        else if (SystemAPI.QueryBuilder().WithAll<NetworkStreamConnection>().Build().IsEmpty)
            driver.Connect(state.EntityManager, NetSession.ConnectTo);
    }
}

/// <summary>
/// Joining. A client, once connected and its scene's ghost prefabs are loaded, goes in game and sends a JoinRequest with
/// its local players and scene (again after every scene change), and its Steam account and name for the
/// leaderboards (kept on its connection, JoinedPlayers). The host takes its own local connection in game at once. A remote one that is new to the game is turned down (JoinRefused) when it would make more than
/// NetSession.MaxPlayers, or when the host isn't in town; one in another scene is sent the host's (LoadSceneRequest) and
/// asks again from there. Otherwise it goes in game. Netcode only runs its prediction loop, and with it physics, once a
/// connection is in game.
/// Couch players on a client join and leave there at once, and the client tells the host (LocalPlayersRequest). The host
/// handles requests one by one, its own couch player counted in, so the game never has more than NetSession.MaxPlayers:
/// one too many is turned down (CouchRefused), and leaves again on its machine.
/// A client that is turned down, can't reach the host or loses it goes back to its own game (NetSession.Leave).
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
public partial struct NetJoinSystem : ISystem
{
    // A client: its connection has had its NetworkId (a world rebuilt around a kept connection has it from the start)
    bool _connected;
    // A client: its local players as the host last heard of them
    LocalPlayers _sent;

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

        // Every granted place counts at once, for the requests after it and for the host's own couch player (CoopHUD)
        int remotePlayers = 0;
        foreach (var joined in SystemAPI.Query<RefRO<JoinedPlayers>>()) remotePlayers += joined.ValueRO.Players.Count;
        HandleJoinRequests(ref state, commandBuffer, ref remotePlayers);
        HandlePlayerChanges(ref state, commandBuffer, ref remotePlayers);
        NetSession.RemotePlayers = remotePlayers;
    }

    void HandleJoinRequests(ref SystemState state, EntityCommandBuffer commandBuffer, ref int remotePlayers)
    {
        var query = SystemAPI.QueryBuilder().WithAll<JoinRequest, ReceiveRpcCommandRequest>().Build();
        if (query.IsEmpty) return;
        using var requests = query.ToComponentDataArray<JoinRequest>(Allocator.Temp);
        using var receives = query.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
        state.EntityManager.DestroyEntity(query);

        string hostScene = SceneManager.GetActiveScene().name;
        for (int i = 0; i < requests.Length; i++)
        {
            var request = requests[i];
            var connection = receives[i].SourceConnection;
            if (!state.EntityManager.Exists(connection)) continue;
            int players = math.clamp(request.Players.Count, 1, GameInput.MaxPlayers);

            // New to the game (not back after a scene change): only into a town, and only while there's room
            bool member = state.EntityManager.HasComponent<JoinedPlayers>(connection);
            if (!member)
            {
                if (GameInput.PlayerCount + remotePlayers + players > NetSession.MaxPlayers) { Refuse(commandBuffer, connection, JoinRefusal.Full); continue; }
                // The main menu counts as town for GameArea, but there's no game to join
                if (!GameArea.IsTown || GameBootstrap.IsMenuScene(hostScene)) { Refuse(commandBuffer, connection, JoinRefusal.NotInTown); continue; }
            }
            // Somewhere else: to the host's scene first, which asks again from there
            if (request.Scene.ToString() != hostScene)
            {
                Send(commandBuffer, connection, new LoadSceneRequest { Scene = hostScene, Seed = LevelSeed.Current });
                continue;
            }

            // The client's world is new (after a scene change too): it has no ghost prefabs until it says so, so the
            // host sends it the prefab list before any ghosts
            var ack = state.EntityManager.GetComponentData<NetworkSnapshotAck>(connection);
            ack.NumLoadedPrefabs = 0;
            state.EntityManager.SetComponentData(connection, ack);
            state.EntityManager.AddComponent<NetworkStreamInGame>(connection);
            if (!member) state.EntityManager.AddComponentData(connection, new JoinedPlayers { PlatformId = request.PlatformId, Name = request.Name });
            SetPlayers(ref state, commandBuffer, connection, request.Players, ref remotePlayers);
        }
    }

    // A member's couch player joined, left or picked a character
    void HandlePlayerChanges(ref SystemState state, EntityCommandBuffer commandBuffer, ref int remotePlayers)
    {
        var query = SystemAPI.QueryBuilder().WithAll<LocalPlayersRequest, ReceiveRpcCommandRequest>().Build();
        if (query.IsEmpty) return;
        using var requests = query.ToComponentDataArray<LocalPlayersRequest>(Allocator.Temp);
        using var receives = query.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
        state.EntityManager.DestroyEntity(query);
        for (int i = 0; i < requests.Length; i++)
        {
            var connection = receives[i].SourceConnection;
            // Not in the game yet: its JoinRequest carries its players
            if (!state.EntityManager.HasComponent<JoinedPlayers>(connection)) continue;
            SetPlayers(ref state, commandBuffer, connection, requests[i].Players, ref remotePlayers);
        }
    }

    // A member's players from now on; without its couch player when that would be one too many
    static void SetPlayers(ref SystemState state, EntityCommandBuffer commandBuffer, Entity connection, LocalPlayers players, ref int remotePlayers)
    {
        var joined = state.EntityManager.GetComponentData<JoinedPlayers>(connection);
        players.Count = math.clamp(players.Count, 1, GameInput.MaxPlayers);
        int others = GameInput.PlayerCount + remotePlayers - joined.Players.Count;
        if (players.Count > joined.Players.Count && others + players.Count > NetSession.MaxPlayers)
        {
            players = new LocalPlayers { Count = 1 };
            Send(commandBuffer, connection, new CouchRefused());
        }
        remotePlayers += players.Count - joined.Players.Count;
        joined.Players = players;
        state.EntityManager.SetComponentData(connection, joined);
    }

    static FixedString32Bytes Truncated(string text)
    {
        FixedString32Bytes fixedText = default;
        fixedText.CopyFromTruncated(text ?? "");
        return fixedText;
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

        // No place for the couch player: they leave again
        foreach (var (_, rpc) in SystemAPI.Query<RefRO<CouchRefused>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
        {
            commandBuffer.DestroyEntity(rpc);
            CoopManager.Leave(1);
            NetSession.ShowNotice($"That game is full ({NetSession.MaxPlayers} players at most).");
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
        var mine = LocalPlayers.Mine;
        foreach (var (_, connection) in SystemAPI.Query<RefRO<NetworkId>>().WithAll<NetworkStreamInGame>().WithEntityAccess())
        {
            if (mine.Same(_sent)) continue;
            _sent = mine;
            Send(commandBuffer, connection, new LocalPlayersRequest { Players = mine });
        }
        foreach (var (_, connection) in SystemAPI.Query<RefRO<NetworkId>>().WithNone<NetworkStreamInGame>().WithEntityAccess())
        {
            commandBuffer.AddComponent<NetworkStreamInGame>(connection);
            _sent = mine;
            Send(commandBuffer, connection, new JoinRequest
            {
                Players = mine,
                Scene = SceneManager.GetActiveScene().name,
                PlatformId = Platform.Current.PlayerId,
                Name = Truncated(Leaderboards.MachineName()),
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
