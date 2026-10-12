using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
public partial struct CalculateRageSystem : ISystem
{
    private const float Resource = 40;
    private const float ResourceRegeneration = -3;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var stats in SystemAPI.Query<RefRW<CalculatedStats>>().WithPresent<Resource, Rage>())
        {
            stats.ValueRW.Value.Resource += Resource;
            stats.ValueRW.Value.ResourceRegeneration += ResourceRegeneration;
        }
    }
}


[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateBefore(typeof(DamagingSystem))]
public partial struct RageSystem : ISystem
{  
    private const float ResourcePerHit = 3;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (rage, resourceChanger, calculatedDamageBuffer) in SystemAPI.Query<RefRO<Rage>, DynamicBuffer<ResourceChanger>, DynamicBuffer<CalculatedDamageInstance>>())
        {
            foreach(var instance in calculatedDamageBuffer)
            {
                if(instance.Hierarchy == DamageHierarchy.Main)
                {
                    resourceChanger.Add(new ResourceChanger{Value = ResourcePerHit});
                }
                
            }
        }
    }
}