using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

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

    /// <summary>On a client: its own player ghost for this local player, or Entity.Null while it hasn't arrived.</summary>
    public static Entity FindOwned(EntityManager entityManager, int playerIndex)
    {
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<NetPlayer>(), ComponentType.ReadOnly<GhostOwnerIsLocal>());
        using var entities = query.ToEntityArray(Allocator.Temp);
        foreach (var entity in entities)
            if (entityManager.GetComponentData<NetPlayer>(entity).LocalIndex == playerIndex) return entity;
        return Entity.Null;
    }

    /// <summary>On a client: what a local player's entity has for the camera, the HUD and reading its input.</summary>
    public static void AddLocalPlayerComponents(EntityManager entityManager, Entity player, int playerIndex)
    {
        UnitEntitySetup.AddOrSet(entityManager, player, new Player());
        UnitEntitySetup.AddOrSet(entityManager, player, new PlayerIndex { Value = playerIndex });
        UnitEntitySetup.AddOrSet(entityManager, player, new InputsData());
        UnitEntitySetup.AddOrSet(entityManager, player, new DesiredDirection());
    }
}
