using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Desperation: below StartHealth of its max health the wearer's cooldowns tick faster, rising evenly to MaxSpeedUp at
/// FullHealth and below. Ticking (1 + speed-up) times as fast is the same as cooldowns 1 / (1 + speed-up) as long, so it
/// adds the matching cooldown reduction.
/// </summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct DesperationStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (desperation, health, stats) in SystemAPI.Query<RefRO<Desperation>, RefRO<Health>, RefRW<CalculatedStats>>())
        {
            var d = desperation.ValueRO;
            if (health.ValueRO.MaxValue <= 0f || d.StartHealth <= d.FullHealth) continue;
            float fraction = health.ValueRO.CurrentValue / health.ValueRO.MaxValue;
            float speedUp = d.MaxSpeedUp * math.saturate((d.StartHealth - fraction) / (d.StartHealth - d.FullHealth));
            stats.ValueRW.Value.CooldownReduction += 1f - 1f / (1f + speedUp);
        }
    }
}
