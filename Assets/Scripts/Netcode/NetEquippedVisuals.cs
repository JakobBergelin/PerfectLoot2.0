using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

/// <summary>On a player ghost in someone else's game: the EquippedVisual list it last had (NetEquippedVisualsSystem).</summary>
public struct NetEquippedVisualsSeen : IComponentData
{
    public uint Hash;
}

/// <summary>
/// In someone else's game: a player's EquippedVisual list comes from the host. When it changes, this asks
/// EquipmentVisualSystem to rebuild the player's parts (EquipmentVisualsChanged), as InventoryManager does on the host.
/// </summary>
[UpdateInGroup(typeof(FeedbackSystemGroup), OrderFirst = true)]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial class NetEquippedVisualsSystem : SystemBase
{
    protected override void OnUpdate()
    {
        if (NetSession.IsHost) return;

        var changed = new NativeList<(Entity Entity, uint Hash)>(Allocator.Temp);
        foreach (var (visuals, entity) in SystemAPI.Query<DynamicBuffer<EquippedVisual>>().WithAll<GhostInstance>().WithEntityAccess())
        {
            uint hash = (uint)visuals.Length;
            foreach (var visual in visuals) hash = math.hash(new uint2(hash, (uint)visual.Key.GetHashCode()));
            if (SystemAPI.HasComponent<NetEquippedVisualsSeen>(entity) && SystemAPI.GetComponent<NetEquippedVisualsSeen>(entity).Hash == hash) continue;
            changed.Add((entity, hash));
        }

        foreach (var (entity, hash) in changed)
        {
            if (EntityManager.HasComponent<NetEquippedVisualsSeen>(entity)) EntityManager.SetComponentData(entity, new NetEquippedVisualsSeen { Hash = hash });
            else EntityManager.AddComponentData(entity, new NetEquippedVisualsSeen { Hash = hash });
            if (!EntityManager.HasComponent<EquipmentVisualsChanged>(entity)) EntityManager.AddComponent<EquipmentVisualsChanged>(entity);
        }
        changed.Dispose();
    }
}
