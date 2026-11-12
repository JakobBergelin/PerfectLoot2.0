using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// On the host: makes the player ghosts of each remote client that joined (JoinedPlayers), on the host's first player,
/// set up by the same PlayerEntitySetup as the host's own, and owned by that client's connection. The first time at
/// once; one that went missing (died) is made again after RespawnDelay. Each takes the next free flow field slot after the
/// host's couch players. Their loadout is the client's own, sent once it has the ghost (NetLoadoutReceiveSystem). The
/// players of a client that left or lost its connection are removed.
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class NetPlayerSpawnSystem : SystemBase
{
    const float RespawnDelay = 3f;

    /// <summary>On a remote client's connection: seconds until its missing players may be made.</summary>
    struct RespawnTimer : IComponentData
    {
        public float Remaining;
        public bool Spawned;    // Its players have been made once
        public bool Waiting;    // Counting down to remake a missing one
    }

    protected override void OnCreate()
    {
        RequireForUpdate<PlayerPrefab>();
        // A new scene's world, built around the kept connections: their players are made at once again
        using var timers = EntityManager.CreateEntityQuery(typeof(RespawnTimer));
        EntityManager.RemoveComponent<RespawnTimer>(timers);
    }

    protected override void OnUpdate()
    {
        RemoveLeftPlayers();
        var entityManager = EntityManager;
        var host = CharacterManager.For(0);
        if (host == null || host.Player == Entity.Null || !entityManager.Exists(host.Player)) return;
        // A generated level must exist first, or players would fall through it
        if (!LevelGeneration.LevelState.IsReady(entityManager)) return;

        using var connections = SystemAPI.QueryBuilder().WithAll<NetworkId, JoinedPlayers, NetworkStreamInGame>().Build().ToEntityArray(Allocator.Temp);
        foreach (var connection in connections)
        {
            int networkId = entityManager.GetComponentData<NetworkId>(connection).Value;
            int count = entityManager.GetComponentData<JoinedPlayers>(connection).Count;
            if (!entityManager.HasComponent<RespawnTimer>(connection)) entityManager.AddComponentData(connection, new RespawnTimer());
            var timer = entityManager.GetComponentData<RespawnTimer>(connection);

            // The first time at once; after a player went missing (died), RespawnDelay later
            bool missing = !AllPresent(networkId, count);
            if (missing && timer.Spawned && !timer.Waiting)
            {
                timer.Remaining = RespawnDelay;
                timer.Waiting = true;
            }
            timer.Remaining = math.max(0f, timer.Remaining - SystemAPI.Time.DeltaTime);
            if (missing && timer.Remaining <= 0f)
            {
                for (int localIndex = 0; localIndex < count; localIndex++)
                    if (!HasPlayer(networkId, localIndex)) Spawn(host, networkId, localIndex);
                timer.Spawned = true;
                timer.Waiting = false;
            }
            entityManager.SetComponentData(connection, timer);
        }
    }

    // Players whose client is gone (left, lost the connection) go with it
    void RemoveLeftPlayers()
    {
        var connected = new NativeHashSet<int>(NetSession.MaxPlayers, Allocator.Temp);
        foreach (var id in SystemAPI.Query<RefRO<NetworkId>>()) connected.Add(id.ValueRO.Value);
        var left = new NativeList<Entity>(Allocator.Temp);
        foreach (var (owner, entity) in SystemAPI.Query<RefRO<GhostOwner>>().WithAll<RemotePlayer>().WithEntityAccess())
            if (!connected.Contains(owner.ValueRO.NetworkId)) left.Add(entity);
        if (left.Length > 0)
        {
            EntityManager.DestroyEntity(left.AsArray());
            Debug.Log($"NetPlayerSpawnSystem: {left.Length} player(s) of a client that left removed");
        }
        connected.Dispose();
        left.Dispose();
    }

    bool HasPlayer(int networkId, int localIndex)
    {
        foreach (var (owner, player) in SystemAPI.Query<RefRO<GhostOwner>, RefRO<NetPlayer>>().WithAll<RemotePlayer>())
            if (owner.ValueRO.NetworkId == networkId && player.ValueRO.LocalIndex == localIndex) return true;
        return false;
    }

    bool AllPresent(int networkId, int count)
    {
        for (int i = 0; i < count; i++)
            if (!HasPlayer(networkId, i)) return false;
        return true;
    }

    void Spawn(CharacterManager host, int networkId, int localIndex)
    {
        var entityManager = EntityManager;
        var hostPosition = entityManager.GetComponentData<LocalTransform>(host.Player).Position;
        var player = entityManager.Instantiate(SystemAPI.GetSingleton<PlayerPrefab>().Value);
        var transform = entityManager.GetComponentData<LocalTransform>(player);
        // On the host player (players don't block each other), so never inside a wall next to it
        transform.Position = hostPosition + new float3(0.3f * (localIndex + 1), 0f, 0f);
        entityManager.SetComponentData(player, transform);
        entityManager.SetComponentData(player, new GhostOwner { NetworkId = networkId });
        entityManager.SetComponentData(player, new NetPlayer { LocalIndex = localIndex });

        var baseStats = host.BaseStats != null ? host.BaseStats.Stats : ScriptableObject.CreateInstance<Characters.PlayerBaseStats>().Stats;
        PlayerEntitySetup.AddGameplayComponents(entityManager, player, baseStats, FreeFieldSlot());
        entityManager.AddComponentData(player, new RemotePlayer());
        // Its class, skills, passives and items come from the client once it has the ghost (NetLoadoutReceiveSystem)
        Debug.Log($"NetPlayerSpawnSystem: player {localIndex + 1} of connection {networkId}");
    }

    // Flow field slots 0 and 1 are the host's couch players; remote players take a free one from the top, so slot 1 is only
    // taken by a fourth player while the host plays alone (and then nobody more can join on the couch: MaxPlayers)
    int FreeFieldSlot()
    {
        int used = 0;
        foreach (var targetable in SystemAPI.Query<RefRO<EnemyTargetable>>().WithAll<Player>())
            used |= 1 << targetable.ValueRO.FieldSlot;
        for (int slot = FlowFields.MaxFields - 1; slot >= 1; slot--)
            if ((used & (1 << slot)) == 0) return slot;
        return FlowFields.MaxFields - 1;
    }
}
