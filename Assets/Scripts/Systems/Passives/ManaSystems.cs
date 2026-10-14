using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
public partial struct CalculateManaSystem : ISystem
{

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (mana, stats) in SystemAPI.Query<RefRO<Mana>, RefRW<CalculatedStats>>().WithPresent<Resource>())
        {
            stats.ValueRW.Value.Resource += mana.ValueRO.MaxResource;
            stats.ValueRW.Value.ResourceRegeneration += mana.ValueRO.Regeneration;
        }
    }
}
