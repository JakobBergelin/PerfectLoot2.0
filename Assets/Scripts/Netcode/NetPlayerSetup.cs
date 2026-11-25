using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

/// <summary>What a player ghost needs on the machine that owns it, shared by CharacterManager on hosts and clients.</summary>
public static class NetPlayerSetup
{
    /// <summary>
    /// On a host: the player is owned by the host's own connection, as this local player, so its input goes through the
    /// same command as a remote player's.
    /// </summary>
    public static void MarkLocal(EntityManager entityManager, Entity player, int playerIndex)
    {
        if (entityManager.HasComponent<NetPlayer>(player)) entityManager.SetComponentData(player, new NetPlayer { LocalIndex = playerIndex });
        if (!entityManager.HasComponent<GhostOwner>(player)) return;
        using var local = entityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<LocalConnection>());
        if (local.IsEmpty) return;
        entityManager.SetComponentData(player, new GhostOwner { NetworkId = local.GetSingleton<NetworkId>().Value });
    }

    /// <summary>On a client: its own player ghost for this local player's character, or Entity.Null while it hasn't arrived.</summary>
    public static Entity FindOwned(EntityManager entityManager, int playerIndex, int generation)
    {
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<NetPlayer>(), ComponentType.ReadOnly<GhostOwnerIsLocal>());
        using var entities = query.ToEntityArray(Allocator.Temp);
        foreach (var entity in entities)
        {
            var player = entityManager.GetComponentData<NetPlayer>(entity);
            if (player.LocalIndex == playerIndex && player.Generation == generation) return entity;
        }
        return Entity.Null;
    }

    /// <summary>On the host: the NetworkId of its own connection (its players' GhostOwner), 0 without one.</summary>
    public static int HostNetworkId(EntityManager entityManager)
    {
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<LocalConnection>());
        return query.IsEmpty ? 0 : query.GetSingleton<NetworkId>().Value;
    }

    /// <summary>
    /// On the host: the living player a (re)spawning player of this machine goes next to: one on the same machine, who
    /// shares their camera, else (with otherMachines) the first of the others, the host's. False when there's nobody.
    /// </summary>
    public static bool TryFindPartner(EntityManager entityManager, int networkId, bool otherMachines, out float3 position)
    {
        position = default;
        int best = int.MaxValue;
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>(), ComponentType.ReadOnly<NetPlayer>(),
            ComponentType.ReadOnly<GhostOwner>(), ComponentType.ReadOnly<Health>(), ComponentType.ReadOnly<LocalTransform>());
        using var entities = query.ToEntityArray(Allocator.Temp);
        foreach (var entity in entities)
        {
            int owner = entityManager.GetComponentData<GhostOwner>(entity).NetworkId;
            if (owner == 0 || (owner != networkId && !otherMachines)) continue;
            if (entityManager.GetComponentData<Health>(entity).CurrentValue <= 0f) continue;
            int order = owner == networkId ? -1 : owner * GameInput.MaxPlayers + entityManager.GetComponentData<NetPlayer>(entity).LocalIndex;
            if (order >= best) continue;
            best = order;
            position = entityManager.GetComponentData<LocalTransform>(entity).Position;
        }
        return best < int.MaxValue;
    }

    /// <summary>On a client: what a local player's entity has for the camera, the HUD and reading its input.</summary>
    public static void AddLocalPlayerComponents(EntityManager entityManager, Entity player, int playerIndex)
    {
        UnitEntitySetup.AddOrSet(entityManager, player, new Player());
        UnitEntitySetup.AddOrSet(entityManager, player, new PlayerIndex { Value = playerIndex });
        UnitEntitySetup.AddOrSet(entityManager, player, new InputsData());
        UnitEntitySetup.AddOrSet(entityManager, player, new DesiredDirection());
        // Held within the co-op distance while predicting (CoopTetherPredictionSystem)
        UnitEntitySetup.AddOrSet(entityManager, player, new CoopTether { LastPosition = entityManager.GetComponentData<LocalTransform>(player).Position });
    }
}
