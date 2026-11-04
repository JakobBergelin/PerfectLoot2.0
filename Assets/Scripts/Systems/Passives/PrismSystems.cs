using Unity.Burst;
using Unity.Entities;

/// <summary>
/// Prism: a main hit of the wearer that deals damage with a different element than its last one applies PrismBuff, or
/// restarts it. A buff rather than a bonus on that one hit, so area skills get it on every enemy they hit. With an
/// elemental conversion every hit is the same element, so it never triggers. Between working out the damage and
/// dealing it, so hits that were stopped don't count.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateBefore(typeof(DamagingSystem))]
[BurstCompile]
public partial struct PrismSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Prism>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var prismLookup = SystemAPI.GetComponentLookup<Prism>();
        var buffLookup = SystemAPI.GetComponentLookup<PrismBuff>();
        var statsLookup = SystemAPI.GetComponentLookup<CalculatedStats>(true);
        state.CompleteDependency();  // The lookups are used here on the main thread; TimedEffectSystem ticks the buff in a job

        foreach (var taken in SystemAPI.Query<DynamicBuffer<CalculatedDamageInstance>>())
        {
            foreach (var instance in taken)
            {
                if (instance.Hierarchy != DamageHierarchy.Main || instance.Damage <= 0f || !prismLookup.HasComponent(instance.Source)) continue;

                var prism = prismLookup.GetRefRW(instance.Source);
                bool changed = prism.ValueRO.HasElement && prism.ValueRO.LastElement != instance.Type;
                prism.ValueRW.HasElement = true;
                prism.ValueRW.LastElement = instance.Type;
                if (!changed || !buffLookup.HasComponent(instance.Source)) continue;

                float duration = statsLookup.HasComponent(instance.Source)
                    ? EffectApply.BuffDuration(prism.ValueRO.Duration, statsLookup[instance.Source].Value)
                    : prism.ValueRO.Duration;
                if (duration > 0f)
                    EffectApply.Refresh(buffLookup, instance.Source, duration).ValueRW.DamageBonus = prism.ValueRO.DamageBonus;
            }
        }
    }
}

/// <summary>Adds the Prism buff's damage while it is active.</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct PrismBuffStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (buff, stats) in SystemAPI.Query<RefRO<PrismBuff>, RefRW<CalculatedStats>>())
            stats.ValueRW.Value.Damage += buff.ValueRO.DamageBonus;
    }
}
