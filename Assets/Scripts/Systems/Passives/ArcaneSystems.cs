using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
public partial struct CalculateArcaneSystem : ISystem
{
    private const float Resource = 100;
    private const float ResourceRegeneration = 1;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (arcane, stats) in SystemAPI.Query<RefRO<Arcane>, RefRW<CalculatedStats>>().WithPresent<Resource>())
        {
            stats.ValueRW.Value.Resource += Resource;
            stats.ValueRW.Value.ResourceRegeneration += ResourceRegeneration;
        }
    }
}
