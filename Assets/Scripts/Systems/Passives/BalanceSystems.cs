using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(DerivedStatsSystemGroup))]
public partial struct CalculateBalanceSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (balance, stats) in SystemAPI.Query<RefRO<Balance>, RefRW<CalculatedStats>>())
        {
            float min = math.min(stats.ValueRO.Value.Strength, stats.ValueRO.Value.Agility);
            min = math.min(min, stats.ValueRO.Value.Intelligence);
            stats.ValueRW.Value.Damage += min * 0.4f;
        }
    }
}
