using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
public partial struct CalculateRageSystem : ISystem
{

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (rage, stats) in SystemAPI.Query<RefRO<Rage>, RefRW<CalculatedStats>>().WithPresent<Resource>())
        {
            stats.ValueRW.Value.Resource += rage.ValueRO.MaxResource;
            stats.ValueRW.Value.ResourceRegeneration += rage.ValueRO.Regeneration;
        }
    }
}


[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateBefore(typeof(DamagingSystem))]
public partial struct RageSystem : ISystem
{  

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (rage, resourceChanger, calculatedDamageBuffer) in SystemAPI.Query<RefRO<Rage>, DynamicBuffer<ResourceChanger>, DynamicBuffer<CalculatedDamageInstance>>())
        {
            foreach(var instance in calculatedDamageBuffer)
            {
                if(instance.Hierarchy == DamageHierarchy.Main)
                {
                    resourceChanger.Add(new ResourceChanger{Value = rage.ValueRO.ResourcePerHit});
                }
                
            }
        }
    }
}