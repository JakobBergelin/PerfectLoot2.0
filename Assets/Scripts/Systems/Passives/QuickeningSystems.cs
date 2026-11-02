using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Quickening: adds Agility × k / (1 + Agility × k) to CooldownReduction. After the flat stats, so it reads the final
/// Agility (class, level, equipment).
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(DerivedStatsSystemGroup))]
public partial struct CalculateQuickeningSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (quickening, stats) in SystemAPI.Query<RefRO<Quickening>, RefRW<CalculatedStats>>())
        {
            float scaled = math.max(0f, stats.ValueRO.Value.Agility * quickening.ValueRO.CooldownPerAgility);
            stats.ValueRW.Value.CooldownReduction += scaled / (1f + scaled);
        }
    }
}
