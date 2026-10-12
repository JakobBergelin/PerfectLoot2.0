using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
public partial struct CalculateFocusSystem : ISystem
{
    private const float Resource = 75;
    private const float ResourceRegeneration = 0;

    private const float ResourceRegenerationMultiplier = 0.2f;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (resource, stats, entity) in SystemAPI.Query<RefRW<Resource>, RefRW<CalculatedStats>>().WithPresent<Focus>().WithEntityAccess())
        {
            stats.ValueRW.Value.Resource += Resource;
            stats.ValueRW.Value.ResourceRegeneration += ResourceRegeneration;

            if(SystemAPI.HasComponent<StandingStill>(entity) || SystemAPI.HasComponent<Channeling>(entity))
            {
                resource.ValueRW.RegenerationMultiplier += ResourceRegenerationMultiplier;
            }
        }
    }
}