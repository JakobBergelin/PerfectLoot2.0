using Unity.Burst;
using Unity.Entities;

/// <summary>Elemental conversion: marks the wearer's stats so its skills deal the power's element (InitializeFromSourceSystem, tooltips).</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct ElementalConversionSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (conversion, stats) in SystemAPI.Query<RefRO<ElementalConversion>, RefRW<CalculatedStats>>())
        {
            stats.ValueRW.Value.ConvertsDamage = true;
            stats.ValueRW.Value.ConvertDamageTo = conversion.ValueRO.Type;
        }
    }
}
