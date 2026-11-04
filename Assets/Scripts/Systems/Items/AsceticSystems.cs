using Unity.Burst;
using Unity.Entities;

/// <summary>Ascetic legendary power, added by AsceticPower while the item is equipped.</summary>
public struct Ascetic : IComponentData
{
    public float GeneratorBonus;    // 0.25 = +25% damage after a Generator skill
    public float CostPenalty;       // 0.1 = -10% damage after another skill with a resource cost
}

/// <summary>
/// Ascetic: hits don't know which skill made them, so the wearer's damage follows the last skill it used. A skill tagged
/// Generator turns on +GeneratorBonus and any other skill with a resource cost -CostPenalty, until a skill of the other
/// kind is used; other free skills change nothing. Attacks take the stats on the frame after the cast, so they already
/// get the new value. A reaction, so it sees this frame's uses.
/// </summary>
[UpdateInGroup(typeof(SkillReactionSystemGroup))]
[BurstCompile]
public partial struct AsceticSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Ascetic>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var buffLookup = SystemAPI.GetComponentLookup<AsceticBuff>();

        foreach (var (ascetic, skills, entity) in SystemAPI.Query<RefRO<Ascetic>, DynamicBuffer<Skill>>().WithEntityAccess())
        {
            if (!buffLookup.HasComponent(entity)) continue;

            bool generator = false, paid = false;
            foreach (var skill in skills)
            {
                if (!skill.events.OnUsed) continue;
                if ((skill.Tags & SkillTags.Generator) != 0) generator = true;
                else if (skill.State.ResourceCost > 0f || skill.State.ResourceCostPerSecond > 0f) paid = true;
            }
            if (!generator && !paid) continue;

            buffLookup[entity] = new AsceticBuff { DamageBonus = generator ? ascetic.ValueRO.GeneratorBonus : -ascetic.ValueRO.CostPenalty };
            buffLookup.SetComponentEnabled(entity, true);
        }
    }
}

/// <summary>Adds the Ascetic buff's damage while it is active (negative after a skill with a cost).</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct AsceticBuffStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (buff, stats) in SystemAPI.Query<RefRO<AsceticBuff>, RefRW<CalculatedStats>>())
            stats.ValueRW.Value.Damage += buff.ValueRO.DamageBonus;
    }
}
