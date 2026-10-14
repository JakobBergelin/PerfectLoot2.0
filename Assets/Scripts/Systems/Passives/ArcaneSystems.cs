using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
public partial struct CalculateArcaneSystem : ISystem
{

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (arcane, stats) in SystemAPI.Query<RefRO<Arcane>, RefRW<CalculatedStats>>().WithPresent<Resource>())
        {
            stats.ValueRW.Value.Resource += arcane.ValueRO.MaxResource;
            stats.ValueRW.Value.ResourceRegeneration += arcane.ValueRO.Regeneration;
        }
    }
}
