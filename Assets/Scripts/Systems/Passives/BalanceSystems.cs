using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(CalculateStatsSystemGroup))]
public partial struct CalculateBalanceSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (balance, stats, statPercentages) in SystemAPI.Query<RefRO<Balance>, RefRO<CalculatedStats>, RefRW<CalculatedStatsPercentages>>())
        {
            float min = math.min(stats.ValueRO.Strength, stats.ValueRO.Agility);
            min = math.min(min, stats.ValueRO.Intelligence);
            statPercentages.ValueRW.Damage += min * 0.4f;
        }
    }
}
