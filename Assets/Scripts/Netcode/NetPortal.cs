using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

/// <summary>A client to the host: its local player chose the portal (scene) they stand at.</summary>
public struct PortalChoiceRpc : IRpcCommand
{
    public byte LocalIndex;
    public FixedString64Bytes Scene;
}

/// <summary>The host to a client: the dungeon is finished; its players' characters record it (AccountManager).</summary>
public struct DungeonCompletedRpc : IRpcCommand
{
    public FixedString64Bytes DungeonId;
}

/// <summary>The host to a client: how the dungeon run stands, for its DungeonHUD (DungeonManager.ApplyStatus).</summary>
public struct DungeonStatusRpc : IRpcCommand
{
    public byte State;          // DungeonManager.RunState
    public byte Step;
    public int Kills;
    public int TotalEnemies;
    public int Deaths;
    public bool OutOfTime;
    public float TimeLeft;
    public float Survived;
}

/// <summary>The host to a client: the dungeon's return portal opened here (the client places its own, NetPortal.PlaceReturnPortal).</summary>
public struct ReturnPortalRpc : IRpcCommand
{
    public float3 Position;
    public quaternion Rotation;
    public FixedString64Bytes Scene;
    public FixedString64Bytes DisplayName;
}

/// <summary>
/// Portals with several players, couch and online alike: every living player has to choose the portal they stand at. A
/// choice is kept on the player's ghost (NetPlayerActions.Portal), so every machine can show who is ready: the host sets
/// it for its own players (Choose) and for a client's when its PortalChoiceRpc arrives (NetPortalReceiveSystem), and
/// clears it when the player walks away (PortalHUD). Also the dungeon's messages to clients: completion and the return
/// portal, which only the host makes (DungeonManager).
/// </summary>
public static class NetPortal
{
    /// <summary>This machine's local player chooses the portal they stand at: on the host at once, from a client through the host.</summary>
    public static void Choose(int playerIndex, FixedString64Bytes scene)
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;
        var entityManager = world.EntityManager;
        if (NetSession.IsHost)
        {
            var characters = CharacterManager.For(playerIndex);
            if (characters != null) SetChoice(entityManager, characters.Player, scene);
            return;
        }
        var rpc = entityManager.CreateEntity();
        entityManager.AddComponentData(rpc, new PortalChoiceRpc { LocalIndex = (byte)playerIndex, Scene = scene });
        entityManager.AddComponent<SendRpcCommandRequest>(rpc);
    }

    /// <summary>The host: sets (or with default, clears) a player's portal choice.</summary>
    public static void SetChoice(EntityManager entityManager, Entity player, FixedString64Bytes scene)
    {
        if (player == Entity.Null || !entityManager.Exists(player) || !entityManager.HasComponent<NetPlayerActions>(player)) return;
        var actions = entityManager.GetComponentData<NetPlayerActions>(player);
        if (actions.Portal == scene) return;
        actions.Portal = scene;
        entityManager.SetComponentData(player, actions);
    }

    /// <summary>The nearest portal whose range this position is in (along the ground).</summary>
    public static bool TryFindPortal(EntityManager entityManager, float3 position, out Portal nearest, out float3 nearestPosition)
    {
        nearest = default;
        nearestPosition = default;
        using var portalQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Portal>(), ComponentType.ReadOnly<LocalToWorld>());
        using var portals = portalQuery.ToComponentDataArray<Portal>(Allocator.Temp);
        using var transforms = portalQuery.ToComponentDataArray<LocalToWorld>(Allocator.Temp);
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < portals.Length; i++)
        {
            float distance = math.distance(position.xz, transforms[i].Position.xz);
            if (distance > portals[i].Range || distance >= nearestDistance) continue;
            nearestDistance = distance;
            nearest = portals[i];
            nearestPosition = transforms[i].Position;
        }
        return nearestDistance < float.MaxValue;
    }

    /// <summary>The host: tells every other machine in the game.</summary>
    public static void Send<T>(EntityManager entityManager, T message) where T : unmanaged, IRpcCommand
    {
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>(), ComponentType.ReadOnly<NetworkStreamInGame>());
        using var connections = query.ToEntityArray(Allocator.Temp);
        foreach (var connection in connections)
        {
            var rpc = entityManager.CreateEntity();
            entityManager.AddComponentData(rpc, message);
            entityManager.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
        }
    }

    /// <summary>
    /// A return portal from the level's prefab (LevelArea.ReturnPortal), placed and turned, leading to scene: the host's
    /// (DungeonManager), and each client's own copy where the host's is.
    /// </summary>
    public static void PlaceReturnPortal(EntityManager entityManager, Entity prefab, float3 position, quaternion turn, FixedString64Bytes scene, FixedString64Bytes displayName)
    {
        if (prefab == Entity.Null || !entityManager.Exists(prefab)) return;
        var portal = entityManager.Instantiate(prefab);
        if (entityManager.HasComponent<LocalTransform>(portal))
        {
            var transform = entityManager.GetComponentData<LocalTransform>(portal);
            transform.Position = position;
            transform.Rotation = turn;
            entityManager.SetComponentData(portal, transform);
        }
        // Linked parts without a parent are placed themselves (like LevelGenerationSystem's spawns)
        if (entityManager.HasBuffer<LinkedEntityGroup>(portal))
        {
            using var linked = entityManager.GetBuffer<LinkedEntityGroup>(portal).Reinterpret<Entity>().ToNativeArray(Allocator.Temp);
            foreach (var entity in linked)
            {
                if (entity == portal || entityManager.HasComponent<Parent>(entity) || !entityManager.HasComponent<LocalTransform>(entity)) continue;
                var transform = entityManager.GetComponentData<LocalTransform>(entity);
                transform.Position = position + math.mul(turn, transform.Position);
                transform.Rotation = math.mul(turn, transform.Rotation);
                entityManager.SetComponentData(entity, transform);
            }
        }
        if (entityManager.HasComponent<Portal>(portal))
        {
            var data = entityManager.GetComponentData<Portal>(portal);
            data.Scene = scene;
            data.DisplayName = displayName;
            entityManager.SetComponentData(portal, data);
        }
        else Debug.LogWarning("NetPortal: the dungeon's Return Portal has no PortalAuthoring, so it leads nowhere.");
    }
}

/// <summary>The host: a client's portal choice, kept when its player really stands at that portal.</summary>
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial class NetPortalReceiveSystem : SystemBase
{
    EntityQuery _choices;

    protected override void OnCreate()
    {
        _choices = SystemAPI.QueryBuilder().WithAll<PortalChoiceRpc, ReceiveRpcCommandRequest>().Build();
        RequireForUpdate(_choices);
    }

    protected override void OnUpdate()
    {
        using var choices = _choices.ToComponentDataArray<PortalChoiceRpc>(Allocator.Temp);
        using var requests = _choices.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
        EntityManager.DestroyEntity(_choices);
        for (int i = 0; i < choices.Length; i++)
        {
            var connection = requests[i].SourceConnection;
            if (!EntityManager.HasComponent<NetworkId>(connection)) continue;
            int networkId = EntityManager.GetComponentData<NetworkId>(connection).Value;
            var player = FindPlayer(networkId, choices[i].LocalIndex);
            if (player == Entity.Null) continue;
            var position = EntityManager.GetComponentData<LocalToWorld>(player).Position;
            if (NetPortal.TryFindPortal(EntityManager, position, out var portal, out _) && portal.Scene == choices[i].Scene)
                NetPortal.SetChoice(EntityManager, player, choices[i].Scene);
        }
    }

    Entity FindPlayer(int networkId, int localIndex)
    {
        foreach (var (owner, netPlayer, entity) in SystemAPI.Query<RefRO<GhostOwner>, RefRO<NetPlayer>>().WithAll<RemotePlayer, LocalToWorld>().WithEntityAccess())
            if (owner.ValueRO.NetworkId == networkId && netPlayer.ValueRO.LocalIndex == localIndex) return entity;
        return Entity.Null;
    }
}

/// <summary>In someone else's game: the host's dungeon messages (how the run stands, completion, the return portal).</summary>
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial class NetDungeonReceiveSystem : SystemBase
{
    EntityQuery _status, _completed, _portals;

    protected override void OnCreate()
    {
        _status = SystemAPI.QueryBuilder().WithAll<DungeonStatusRpc, ReceiveRpcCommandRequest>().Build();
        _completed = SystemAPI.QueryBuilder().WithAll<DungeonCompletedRpc, ReceiveRpcCommandRequest>().Build();
        _portals = SystemAPI.QueryBuilder().WithAll<ReturnPortalRpc, ReceiveRpcCommandRequest>().Build();
        RequireAnyForUpdate(_status, _completed, _portals);
    }

    protected override void OnUpdate()
    {
        using (var statuses = _status.ToComponentDataArray<DungeonStatusRpc>(Allocator.Temp))
        {
            EntityManager.DestroyEntity(_status);
            // Only the latest counts
            if (statuses.Length > 0 && DungeonManager.Instance != null) DungeonManager.Instance.ApplyStatus(statuses[statuses.Length - 1]);
        }

        using (var completed = _completed.ToComponentDataArray<DungeonCompletedRpc>(Allocator.Temp))
        {
            EntityManager.DestroyEntity(_completed);
            var account = AccountManager.Instance;
            foreach (var message in completed)
                if (account != null && !NetSession.IsHost)
                    for (int i = 0; i < GameInput.PlayerCount; i++)
                        if (account.SelectedCharacterOf(i) != null) account.RecordDungeonCompleted(account.SelectedCharacterOf(i), message.DungeonId.ToString());
        }

        using (var portals = _portals.ToComponentDataArray<ReturnPortalRpc>(Allocator.Temp))
        {
            EntityManager.DestroyEntity(_portals);
            if (NetSession.IsHost || !GameArea.TryGet(out var area)) return;
            foreach (var message in portals)
                NetPortal.PlaceReturnPortal(EntityManager, area.ReturnPortal, message.Position, message.Rotation, message.Scene, message.DisplayName);
        }
    }
}
