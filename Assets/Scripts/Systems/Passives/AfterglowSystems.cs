using Unity.Burst;
using Unity.Entities;

/// <summary>Afterglow: using a skill tagged Cooldown applies AfterglowBuff, or restarts it. A reaction, so it sees this frame's uses.</summary>
[UpdateInGroup(typeof(SkillReactionSystemGroup))]
[BurstCompile]
public partial struct AfterglowSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Afterglow>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var buffLookup = SystemAPI.GetComponentLookup<AfterglowBuff>();
        state.CompleteDependency();  // The lookup is used here on the main thread; TimedEffectSystem ticks it in a job

        foreach (var (afterglow, skills, stats, entity) in SystemAPI.Query<RefRO<Afterglow>, DynamicBuffer<Skill>, RefRO<CalculatedStats>>().WithEntityAccess())
        {
            if (!buffLookup.HasComponent(entity)) continue;

            bool used = false;
            foreach (var skill in skills)
                if (skill.events.OnUsed && (skill.Tags & SkillTags.Cooldown) != 0) used = true;
            if (!used) continue;

            float duration = EffectApply.BuffDuration(afterglow.ValueRO.Duration, stats.ValueRO.Value);
            if (duration > 0f)
                EffectApply.Refresh(buffLookup, entity, duration).ValueRW.DamageBonus = afterglow.ValueRO.DamageBonus;
        }
    }
}

/// <summary>Adds the Afterglow buff's damage while it is active.</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct AfterglowBuffStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (buff, stats) in SystemAPI.Query<RefRO<AfterglowBuff>, RefRW<CalculatedStats>>())
            stats.ValueRW.Value.Damage += buff.ValueRO.DamageBonus;
    }
}
