using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(CalculateStatsSystemGroup))]
public partial struct CalculateEnergySystem : ISystem
{
    private const float Resource = 35;
    private const float ResourceRegeneration = 1;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var resource in SystemAPI.Query<RefRW<Resource>>().WithPresent<Energy>())
        {
            resource.ValueRW.MaxValue += Resource;
            resource.ValueRW.Regeneration += ResourceRegeneration;
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