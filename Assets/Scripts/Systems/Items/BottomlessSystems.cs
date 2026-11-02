using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>Bottomless legendary power, added by BottomlessPower while the item is equipped.</summary>
public struct Bottomless : IComponentData
{
    public float MaxCost;   // One-off skill costs up to this (after cost reduction) can always be paid
}

/// <summary>Bottomless: raises the wearer's AlwaysAffordableCost (SkillUtil.CanAfford) to its MaxCost.</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct BottomlessSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (bottomless, stats) in SystemAPI.Query<RefRO<Bottomless>, RefRW<CalculatedStats>>())
            stats.ValueRW.AlwaysAffordableCost = math.max(stats.ValueRO.AlwaysAffordableCost, bottomless.ValueRO.MaxCost);
    }
}
