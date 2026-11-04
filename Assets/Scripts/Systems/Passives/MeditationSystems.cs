using Unity.Burst;
using Unity.Entities;

/// <summary>
/// Meditation: while Channeling, everything the wearer takes is lowered by DamageReduction. Before Medusa, so that
/// absorbs what is left. Between working out the damage and dealing it.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateBefore(typeof(MedusaSystem))]
[UpdateBefore(typeof(DamagingSystem))]
[BurstCompile]
public partial struct MeditationSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Meditation>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (meditation, taken) in SystemAPI.Query<RefRO<Meditation>, DynamicBuffer<CalculatedDamageInstance>>().WithAll<Channeling>())
        {
            for (int i = 0; i < taken.Length; i++)
                taken.ElementAt(i).Damage *= 1f - meditation.ValueRO.DamageReduction;
        }
    }
}

/// <summary>Meditation: adds its health regeneration while Channeling.</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct MeditationStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (meditation, stats) in SystemAPI.Query<RefRO<Meditation>, RefRW<CalculatedStats>>().WithAll<Channeling>())
            stats.ValueRW.Value.HealthRegeneration += meditation.ValueRO.HealthRegeneration;
    }
}
