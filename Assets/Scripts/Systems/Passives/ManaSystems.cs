using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(CalculateStatsSystemGroup))]
public partial struct CalculateManaSystem : ISystem
{
    private const float Resource = 50;
    private const float ResourceRegeneration = 5;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (mana, resource) in SystemAPI.Query<RefRO<Mana>, RefRW<Resource>>())
        {
            resource.ValueRW.MaxValue += Resource;
            resource.ValueRW.Regeneration += ResourceRegeneration;
        }
    }
}
