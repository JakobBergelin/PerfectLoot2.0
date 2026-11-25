using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// On the host: makes the player ghosts of each remote client that joined (JoinedPlayers), next to the host's first
/// player, set up by the same PlayerEntitySetup as the host's own, and owned by that client's connection. The first time
/// at once; one that went missing (died) is made again after the host's CharacterManager.RespawnDelay, where the host's
/// own would be (CharacterManager.TryFindSpawnPosition). A couch player joining later appears next to their machine's
/// other player. Each takes a free flow field slot after the host's couch players'. Their loadout is the client's own,
/// sent once it has the ghost (NetLoadoutReceiveSystem). The players of a client that left or lost its connection are
/// removed, and a couch player who left or plays another character now.
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class NetPlayerSpawnSystem : SystemBase
{
    /// <summary>A remote client's player died (went missing while its client is connected), like CharacterManager.PlayerDied: once per player.</summary>
    public static event System.Action RemotePlayerDied;

    /// <summary>On a remote client's connection: seconds until its missing players may be made.</summary>
    struct RespawnTimer : IComponentData
    {
        public float Remaining;
        public bool Spawned;    // Its players have been made once
        public bool Waiting;    // Counting down to remake a missing one
        public int Present;     // Its players that were there after last frame, a bit per local player
        public int Couch;       // LocalPlayers.CouchGeneration then
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
        if (host == null) return;
        bool hostPlays = host.Player != Entity.Null && entityManager.Exists(host.Player);
        // A generated level must exist first, or players would fall through it
        if (!LevelGeneration.LevelState.IsReady(entityManager)) return;

        using var connections = SystemAPI.QueryBuilder().WithAll<NetworkId, JoinedPlayers, NetworkStreamInGame>().Build().ToEntityArray(Allocator.Temp);
        foreach (var connection in connections)
        {
            int networkId = entityManager.GetComponentData<NetworkId>(connection).Value;
            var players = entityManager.GetComponentData<JoinedPlayers>(connection).Players;
            if (!entityManager.HasComponent<RespawnTimer>(connection)) entityManager.AddComponentData(connection, new RespawnTimer());
            var timer = entityManager.GetComponentData<RespawnTimer>(connection);

            // A couch player who left or plays another character went without dying
            if (timer.Couch != players.CouchGeneration)
            {
                timer.Couch = players.CouchGeneration;
                timer.Present &= 1;
            }
            int wanted = 0, present = 0;
            for (int i = 0; i < GameInput.MaxPlayers; i++)
            {
                if (!players.Plays(i, out _)) continue;
                wanted |= 1 << i;
                if (HasPlayer(networkId, i)) present |= 1 << i;
            }

            // The first time at once; after a player went missing (died), RespawnDelay later
            int died = timer.Present & ~present;
            if (died != 0)
            {
                for (int i = 0; i < math.countbits(died); i++) RemotePlayerDied?.Invoke();
                if (!timer.Waiting) timer.Remaining = host.RespawnDelay;
                timer.Waiting = true;
            }
            timer.Remaining = math.max(0f, timer.Remaining - SystemAPI.Time.DeltaTime);
            if (present != wanted && timer.Remaining <= 0f && (timer.Spawned || hostPlays))
            {
                for (int i = 0; i < GameInput.MaxPlayers; i++)
                    if ((wanted & ~present & (1 << i)) != 0 && TrySpawn(host, networkId, i, players, timer.Spawned)) present |= 1 << i;
                if (present == wanted)
                {
                    timer.Spawned = true;
                    timer.Waiting = false;
                }
            }
            timer.Present = present;
            entityManager.SetComponentData(connection, timer);
        }
    }

    // Players whose client is gone (left, lost the connection) go with it, and a couch player who left their machine or
    // plays another character now
    void RemoveLeftPlayers()
    {
        var playing = new NativeHashMap<int, LocalPlayers>(NetSession.MaxPlayers, Allocator.Temp);
        foreach (var (id, joined) in SystemAPI.Query<RefRO<NetworkId>, RefRO<JoinedPlayers>>()) playing[id.ValueRO.Value] = joined.ValueRO.Players;
        var left = new NativeList<Entity>(Allocator.Temp);
        foreach (var (owner, player, entity) in SystemAPI.Query<RefRO<GhostOwner>, RefRO<NetPlayer>>().WithAll<RemotePlayer>().WithEntityAccess())
            if (!playing.TryGetValue(owner.ValueRO.NetworkId, out var players) ||
                !players.Plays(player.ValueRO.LocalIndex, out int generation) || generation != player.ValueRO.Generation) left.Add(entity);
        if (left.Length > 0)
        {
            EntityManager.DestroyEntity(left.AsArray());
            Debug.Log($"NetPlayerSpawnSystem: {left.Length} player(s) who left removed");
        }
        playing.Dispose();
        left.Dispose();
    }

    bool HasPlayer(int networkId, int localIndex)
    {
        foreach (var (owner, player) in SystemAPI.Query<RefRO<GhostOwner>, RefRO<NetPlayer>>().WithAll<RemotePlayer>())
            if (owner.ValueRO.NetworkId == networkId && player.ValueRO.LocalIndex == localIndex) return true;
        return false;
    }

    // False while there's nowhere to put it yet
    bool TrySpawn(CharacterManager host, int networkId, int localIndex, in LocalPlayers players, bool respawn)
    {
        var entityManager = EntityManager;
        var prefab = SystemAPI.GetSingleton<PlayerPrefab>().Value;
        // On the other player (players don't block each other), so never inside a wall next to them
        if (NetPlayerSetup.TryFindPartner(entityManager, networkId, !respawn || !DungeonManager.RespawnAtStart, out var position))
            position += new float3(0.3f * (localIndex + 1), 0f, 0f);
        // Or standing at the level's start
        else if (!LevelGeneration.LevelState.TryGetPlayerStart(entityManager, out var start) ||
                 !LevelGeneration.GroundPlacement.TryGetCollisionWorld(entityManager, out var collisionWorld) ||
                 !LevelGeneration.GroundPlacement.TryPlace(collisionWorld, start, LevelGeneration.GroundPlacement.BottomOffset(entityManager, prefab), out position))
            return false;
        players.Plays(localIndex, out int generation);
        var player = entityManager.Instantiate(prefab);
        var transform = entityManager.GetComponentData<LocalTransform>(player);
        transform.Position = position;
        entityManager.SetComponentData(player, transform);
        entityManager.SetComponentData(player, new GhostOwner { NetworkId = networkId });
        entityManager.SetComponentData(player, new NetPlayer { LocalIndex = localIndex, Generation = generation });

        var baseStats = host.BaseStats != null ? host.BaseStats.Stats : ScriptableObject.CreateInstance<Characters.PlayerBaseStats>().Stats;
        PlayerEntitySetup.AddGameplayComponents(entityManager, player, baseStats, FreeFieldSlot());
        entityManager.AddComponentData(player, new RemotePlayer());
        // Its class, skills, passives and items come from the client once it has the ghost (NetLoadoutReceiveSystem)
        Debug.Log($"NetPlayerSpawnSystem: player {localIndex + 1} of connection {networkId}");
        return true;
    }

    // The first flow field slots are the host's couch players'; remote players take a free one after them
    int FreeFieldSlot()
    {
        int used = 0;
        foreach (var targetable in SystemAPI.Query<RefRO<EnemyTargetable>>().WithAll<Player>())
            used |= 1 << targetable.ValueRO.FieldSlot;
        for (int slot = GameInput.MaxPlayers; slot < FlowFields.MaxFields; slot++)
            if ((used & (1 << slot)) == 0) return slot;
        return FlowFields.MaxFields - 1;
    }
}
