using Unity.Burst;
using Unity.Entities;

// Class, resource type and passive skill components are added/removed by CharacterManager and PassiveSkillManager

[BurstCompile]
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
public partial struct CalculateClassSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (classAttributes, level, calculatedStats) in SystemAPI.Query<RefRO<ClassAttributes>, RefRO<Level>, RefRW<CalculatedStats>>())
        {
            calculatedStats.ValueRW.Value.Strength += level.ValueRO.Value * classAttributes.ValueRO.StrengthPerLevel;
            calculatedStats.ValueRW.Value.Agility += level.ValueRO.Value * classAttributes.ValueRO.AgilityPerLevel;
            calculatedStats.ValueRW.Value.Intelligence += level.ValueRO.Value * classAttributes.ValueRO.IntelligencePerLevel;
        }
    }
}
