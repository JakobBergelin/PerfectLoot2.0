using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
public partial struct CalculateGolemSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (golem, health, armor) in SystemAPI.Query<RefRO<Golem>, RefRW<Health>, RefRW<Armor>>())
        {
            health.ValueRW.MaxHealthMultiplier += 1.0f;
            armor.ValueRW.PhysicalResistanceMultiplier += 1.0f;
        }
    }
}
