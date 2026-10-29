using Unity.Burst;
using Unity.Entities;

/// <summary>Pixie Dust: while Invulnerable, adds its multiplier to health and resource regeneration (finalized in FinalizeStatsSystem).</summary>
[BurstCompile]
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
public partial struct PixieDustSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (pixieDust, healthRegeneration) in SystemAPI.Query<RefRO<PixieDust>, RefRW<HealthRegeneration>>().WithAll<Invulnerable>())
            healthRegeneration.ValueRW.Multiplier += pixieDust.ValueRO.RegenerationMultiplier;

        foreach (var (pixieDust, resource) in SystemAPI.Query<RefRO<PixieDust>, RefRW<Resource>>().WithAll<Invulnerable>())
            resource.ValueRW.RegenerationMultiplier += pixieDust.ValueRO.RegenerationMultiplier;
    }
}
