using Unity.Burst;
using Unity.Entities;

/// <summary>Death's Door legendary power, added by DeathsDoorPower while the item is equipped.</summary>
public struct DeathsDoor : IComponentData
{
    public float HealthThreshold;       // Share of max health below which the buff is on
    public float CriticalChanceBonus;   // Of the buff
}

/// <summary>
/// Death's Door: turns the wearer's DeathsDoorBuff on while its health is below HealthThreshold of its max and off
/// otherwise, then adds the critical chance of every active buff (also one applied as a test effect).
/// </summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct DeathsDoorSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (deathsDoor, health, buff, buffEnabled) in
                 SystemAPI.Query<RefRO<DeathsDoor>, RefRO<Health>, RefRW<DeathsDoorBuff>, EnabledRefRW<DeathsDoorBuff>>().WithPresent<DeathsDoorBuff>())
        {
            buffEnabled.ValueRW = health.ValueRO.MaxValue > 0f
                && health.ValueRO.CurrentValue < health.ValueRO.MaxValue * deathsDoor.ValueRO.HealthThreshold;
            buff.ValueRW.CriticalChanceBonus = deathsDoor.ValueRO.CriticalChanceBonus;
        }

        foreach (var (buff, stats) in SystemAPI.Query<RefRO<DeathsDoorBuff>, RefRW<CalculatedStats>>())
            stats.ValueRW.Value.CriticalChance += buff.ValueRO.CriticalChanceBonus;
    }
}
