using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(CalculateStatsSystemGroup))]
public partial struct CalculateFocusSystem : ISystem
{
    private const float Resource = 75;
    private const float ResourceRegeneration = 0;

    private const float ResourceRegenerationMultiplier = 0.2f;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (resource, entity) in SystemAPI.Query<RefRW<Resource>>().WithPresent<Focus>().WithEntityAccess())
        {
            resource.ValueRW.MaxValue += Resource;
            resource.ValueRW.Regeneration += ResourceRegeneration;

            if(SystemAPI.HasComponent<StandingStill>(entity) || SystemAPI.HasComponent<Channeling>(entity))
            {
                resource.ValueRW.RegenerationMultiplier += ResourceRegenerationMultiplier;
            }
        }
    }
}