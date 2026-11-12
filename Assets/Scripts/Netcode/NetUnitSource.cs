using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

/// <summary>
/// On enemy ghosts: the name of the EnemyDefinition the host made it from (several definitions share one prefab).
/// Set by EnemyEntitySetup; empty on bodies made into something else (a Thrall).
/// </summary>
public struct NetUnitSource : IComponentData
{
    [GhostField] public FixedString64Bytes Key;
}

/// <summary>
/// In someone else's game: gives each enemy ghost what this machine needs to show it, which only the host adds:
/// UnitInfo (its definition, from the enemy lists every machine bakes) and the Enemy tag. Then hovering, aim assist
/// and the unit panel find it. The host's own enemies already have both.
/// </summary>
[UpdateInGroup(typeof(FeedbackSystemGroup), OrderFirst = true)]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct NetUnitSourceSystem : ISystem
{
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<EnemyPrefabElement>();

    public void OnUpdate(ref SystemState state)
    {
        var commandBuffer = new EntityCommandBuffer(Allocator.Temp);
        foreach (var (source, entity) in SystemAPI.Query<RefRO<NetUnitSource>>().WithNone<UnitInfo>().WithEntityAccess())
        {
            var key = source.ValueRO.Key;
            if (key.IsEmpty || !TryFindDefinition(ref state, key, out var definition)) continue;
            commandBuffer.AddComponent(entity, new UnitInfo { Source = definition });
            commandBuffer.AddComponent<Enemy>(entity);
        }
        commandBuffer.Playback(state.EntityManager);
    }

    // Every enemy list: the core one and the level's
    bool TryFindDefinition(ref SystemState state, in FixedString64Bytes key, out UnityObjectRef<UnityEngine.ScriptableObject> definition)
    {
        foreach (var list in SystemAPI.Query<DynamicBuffer<EnemyPrefabElement>>())
            foreach (var element in list)
                if (element.Key == key)
                {
                    definition = element.Definition;
                    return true;
                }
        definition = default;
        return false;
    }
}
