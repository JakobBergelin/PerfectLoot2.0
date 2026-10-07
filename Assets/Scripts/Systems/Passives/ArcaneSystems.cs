using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(CalculateStatsSystemGroup))]
public partial struct CalculateArcaneSystem : ISystem
{
    private const float Resource = 100;
    private const float ResourceRegeneration = 1;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (arcane, resource) in SystemAPI.Query<RefRO<Arcane>, RefRW<Resource>>())
        {
            resource.ValueRW.MaxValue += Resource;
            resource.ValueRW.Regeneration += ResourceRegeneration;
        }
    }
}
