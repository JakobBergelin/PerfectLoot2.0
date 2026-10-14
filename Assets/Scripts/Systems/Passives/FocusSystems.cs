using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
public partial struct CalculateFocusSystem : ISystem
{


    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (focus, resource, stats, entity) in SystemAPI.Query<RefRO<Focus>, RefRW<Resource>, RefRW<CalculatedStats>>().WithEntityAccess())
        {
            stats.ValueRW.Value.Resource += focus.ValueRO.MaxResource;
            stats.ValueRW.Value.ResourceRegeneration += focus.ValueRO.Regeneration;

            if(SystemAPI.HasComponent<StandingStill>(entity) || SystemAPI.HasComponent<Channeling>(entity))
            {
                resource.ValueRW.RegenerationMultiplier += focus.ValueRO.StandingStillRegenerationMultiplier;
            }
        }
    }
}