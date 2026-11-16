using Unity.Burst;
using Unity.Entities;

/// <summary>Keen Focus legendary power, added by KeenFocusPower while the item is equipped.</summary>
public struct KeenFocus : IComponentData
{
    public float ResourceThreshold;     // Share of max resource below which the bonus is on
    public float CriticalChanceBonus;
}

/// <summary>Keen Focus: the wearer has extra critical chance while its resource is below ResourceThreshold of its max.</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct KeenFocusSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (keenFocus, resource, stats) in SystemAPI.Query<RefRO<KeenFocus>, RefRO<Resource>, RefRW<CalculatedStats>>())
        {
            if (resource.ValueRO.MaxValue > 0f && resource.ValueRO.CurrentValue < resource.ValueRO.MaxValue * keenFocus.ValueRO.ResourceThreshold)
                stats.ValueRW.Value.CriticalChance += keenFocus.ValueRO.CriticalChanceBonus;
        }
    }
}
