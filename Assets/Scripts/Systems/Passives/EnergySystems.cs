using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
public partial struct CalculateEnergySystem : ISystem
{

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (energy, stats) in SystemAPI.Query<RefRO<Energy>, RefRW<CalculatedStats>>().WithPresent<Resource>())
        {
            stats.ValueRW.Value.Resource += energy.ValueRO.MaxResource;
            stats.ValueRW.Value.ResourceRegeneration += energy.ValueRO.Regeneration;
        }
    }
}


// Grants resource when a skill was used this frame (skill events are set in SkillSystemGroup)
[UpdateInGroup(typeof(HealthAndResourceSystemGroup))]
public partial struct EnergySystem : ISystem
{  

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (energy, resourcechanger, skillBuffer) in SystemAPI.Query<RefRO<Energy>, DynamicBuffer<ResourceChanger>, DynamicBuffer<Skill>>())
        {
            foreach(var instance in skillBuffer)
            {
                if(instance.events.OnUsed)
                {
                    resourcechanger.Add(new ResourceChanger{Value = energy.ValueRO.ResourceOnSkillUse});
                }
                
            }
        }
    }
}