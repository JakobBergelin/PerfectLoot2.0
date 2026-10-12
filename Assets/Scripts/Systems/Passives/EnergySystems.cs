using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
public partial struct CalculateEnergySystem : ISystem
{
    private const float Resource = 35;
    private const float ResourceRegeneration = 1;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var stats in SystemAPI.Query<RefRW<CalculatedStats>>().WithPresent<Resource, Energy>())
        {
            stats.ValueRW.Value.Resource += Resource;
            stats.ValueRW.Value.ResourceRegeneration += ResourceRegeneration;
        }
    }
}


public partial struct EnergySystem : ISystem
{  
    private const float ResourceOnSkillUse = 2;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (rage, resourcechanger, skillBuffer) in SystemAPI.Query<RefRO<Energy>, DynamicBuffer<ResourceChanger>, DynamicBuffer<Skill>>())
        {
            foreach(var instance in skillBuffer)
            {
                if(instance.events.OnUsed)
                {
                    resourcechanger.Add(new ResourceChanger{Value = ResourceOnSkillUse});
                }
                
            }
        }
    }
}