using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
public partial struct CalculateManaSystem : ISystem
{
    private const float Resource = 50;
    private const float ResourceRegeneration = 5;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (mana, stats) in SystemAPI.Query<RefRO<Mana>, RefRW<CalculatedStats>>().WithPresent<Resource>())
        {
            stats.ValueRW.Value.Resource += Resource;
            stats.ValueRW.Value.ResourceRegeneration += ResourceRegeneration;
        }
    }
}
