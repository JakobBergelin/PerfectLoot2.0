using Unity.Burst;
using Unity.Entities;

/// <summary>Broken Promises legendary power, added by BrokenPromisesPower while the item is equipped.</summary>
public struct BrokenPromises : IComponentData
{
    public int HitsRequired;
    public float Duration;              // Of the buff it applies
    public float CriticalChanceBonus;   // Of the buff it applies

    public int NonCritHits;             // Current streak
}

/// <summary>
/// Counts the owner's non-critical main hits in a row; a crit resets the streak, a full streak applies the
/// BrokenPromisesBuff (BrokenPromisesBuffStatsSystem adds its critical chance).
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateBefore(typeof(DamagingSystem))]
[BurstCompile]
public partial struct BrokenPromisesHitSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var brokenPromisesLookup = SystemAPI.GetComponentLookup<BrokenPromises>();
        var buffLookup = SystemAPI.GetComponentLookup<BrokenPromisesBuff>();
        var statsLookup = SystemAPI.GetComponentLookup<CalculatedStats>(true);
        foreach (var calculatedDamageBuffer in SystemAPI.Query<DynamicBuffer<CalculatedDamageInstance>>())
        {
            foreach (var instance in calculatedDamageBuffer)
            {
                if (instance.Hierarchy != DamageHierarchy.Main || !brokenPromisesLookup.HasComponent(instance.Source)) continue;

                var brokenPromises = brokenPromisesLookup.GetRefRW(instance.Source);
                if (instance.IsCrit)
                {
                    brokenPromises.ValueRW.NonCritHits = 0;
                    continue;
                }

                brokenPromises.ValueRW.NonCritHits++;
                if (brokenPromises.ValueRO.NonCritHits < brokenPromises.ValueRO.HitsRequired) continue;

                brokenPromises.ValueRW.NonCritHits = 0;
                if (!buffLookup.HasComponent(instance.Source)) continue;
                float duration = statsLookup.HasComponent(instance.Source)
                    ? EffectApply.BuffDuration(brokenPromises.ValueRO.Duration, statsLookup[instance.Source].Value)
                    : brokenPromises.ValueRO.Duration;
                if (duration > 0f)
                    EffectApply.Refresh(buffLookup, instance.Source, duration).ValueRW.CriticalChanceBonus = brokenPromises.ValueRO.CriticalChanceBonus;
            }
        }
    }
}
