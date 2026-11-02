using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>Second Wind legendary power, added by SecondWindPower while the item is equipped.</summary>
public struct SecondWind : IComponentData
{
    public float MaxRegenerationBonus;  // Regeneration multiplier added at FullBonusHealth or below
    public float FullBonusHealth;       // Share of max health
}

/// <summary>
/// Second Wind: adds to the health regeneration multiplier the lower the wearer's health is, MaxRegenerationBonus at
/// FullBonusHealth or below fading linearly to nothing at full health (finalized in FinalizeStatsSystem).
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
public partial struct SecondWindSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (secondWind, health, healthRegeneration) in SystemAPI.Query<RefRO<SecondWind>, RefRO<Health>, RefRW<HealthRegeneration>>())
        {
            if (health.ValueRO.MaxValue <= 0f) continue;
            float healthShare = health.ValueRO.CurrentValue / health.ValueRO.MaxValue;
            float missing = math.saturate((1f - healthShare) / math.max(1f - secondWind.ValueRO.FullBonusHealth, 1e-4f));
            healthRegeneration.ValueRW.Multiplier += secondWind.ValueRO.MaxRegenerationBonus * missing;
        }
    }
}
