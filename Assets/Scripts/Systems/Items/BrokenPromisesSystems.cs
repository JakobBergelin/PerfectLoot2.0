using Unity.Burst;
using Unity.Entities;

/// <summary>Broken Promises legendary power, added by BrokenPromisesPower while the item is equipped.</summary>
public struct BrokenPromises : IComponentData
{
    public int HitsRequired;
    public float Duration;
    public float CriticalChanceBonus;

    public int NonCritHits;             // Current streak
    public float ActiveTimeRemaining;   // > 0 while the bonus is active
}

/// <summary>Counts the owner's non-critical main hits in a row; a crit resets the streak, a full streak starts the bonus.</summary>
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
                if (brokenPromises.ValueRO.NonCritHits >= brokenPromises.ValueRO.HitsRequired)
                {
                    brokenPromises.ValueRW.NonCritHits = 0;
                    brokenPromises.ValueRW.ActiveTimeRemaining = brokenPromises.ValueRO.Duration;
                }
            }
        }
    }
}

/// <summary>Adds the critical chance bonus while Broken Promises is active, and counts its time down.</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct BrokenPromisesStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        foreach (var (brokenPromises, stats) in SystemAPI.Query<RefRW<BrokenPromises>, RefRW<CalculatedStats>>())
        {
            if (brokenPromises.ValueRO.ActiveTimeRemaining <= 0f) continue;
            stats.ValueRW.Value.CriticalChance += brokenPromises.ValueRO.CriticalChanceBonus;
            brokenPromises.ValueRW.ActiveTimeRemaining -= deltaTime;
        }
    }
}
