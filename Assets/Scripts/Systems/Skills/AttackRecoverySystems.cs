using Unity.Burst;
using Unity.Entities;

/// <summary>
/// Counts down each caster's AttackRecovery by its attack speed, before the skills that read it. It stops at 0, so time
/// spent not attacking never builds up toward later attacks.
/// </summary>
[UpdateInGroup(typeof(SkillSystemGroup), OrderFirst = true)]
[BurstCompile]
public partial struct AttackRecoverySystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        foreach (var (recovery, stats) in SystemAPI.Query<RefRW<AttackRecovery>, RefRO<CalculatedStats>>())
        {
            // Only while recovering. The frame it ends it may dip a little below 0; the next attack adds onto that, so
            // holding a skill fires at exactly its rate
            if (recovery.ValueRO.Remaining > 0f)
                recovery.ValueRW.Remaining -= deltaTime * stats.ValueRO.Value.AttackSpeed;
        }
    }
}
