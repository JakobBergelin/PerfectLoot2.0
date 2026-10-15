using Unity.Burst;
using Unity.Entities;

/// <summary>Tag on a freshly spawned unit: fill health and resource to max once its stats are calculated.</summary>
public struct FillHealthAndResource : IComponentData {}

[UpdateInGroup(typeof(FinalizeStatsSystemGroup))]
[UpdateAfter(typeof(FinalizeStatsSystem))]
[BurstCompile]
public partial struct FillHealthAndResourceSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var health in SystemAPI.Query<RefRW<Health>>().WithAll<FillHealthAndResource>())
        {
            health.ValueRW.CurrentValue = health.ValueRO.MaxValue;
        }
        foreach (var resource in SystemAPI.Query<RefRW<Resource>>().WithAll<FillHealthAndResource>())
        {
            resource.ValueRW.CurrentValue = resource.ValueRO.MaxValue;
        }

        var query = SystemAPI.QueryBuilder().WithAll<FillHealthAndResource>().Build();
        if (!query.IsEmpty)
        {
            state.EntityManager.RemoveComponent<FillHealthAndResource>(query);
        }
    }
}
