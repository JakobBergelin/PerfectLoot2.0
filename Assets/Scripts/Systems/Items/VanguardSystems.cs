using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(CalculateStatsSystemGroup))]
public partial struct CalculateVanguardSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (vanguard, health) in SystemAPI.Query<RefRO<Vanguard>, RefRW<Health>>())
        {
            health.ValueRW.MaxValue += vanguard.ValueRO.Health;
        }
    }
}

[BurstCompile]
[UpdateAfter(typeof(InventorySystem))]
public partial struct RandomizeVanguardSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (itemBuffer, vanguard) in SystemAPI.Query<DynamicBuffer<InventoryItem>, RefRW<Vanguard>>())
        {
            Guid seed = new Guid();
            foreach(var item in itemBuffer)
            {
                if(item.Type == ItemType.Vanguard)
                {
                    seed = item.Seed;
                    break;
                }
            }
            UnityEngine.Random.InitState(seed.GetHashCode());

            vanguard.ValueRW.Health = UnityEngine.Random.Range(2, 8);

            vanguard.ValueRW.BlockAmount = 1.0f;
 
        }
    }
}

public partial struct VanguardSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (vanguard, damageBuffer) in SystemAPI.Query<RefRO<Vanguard>, DynamicBuffer<CalculatedDamageInstance>>())
        {
            for(var i = 0; i < damageBuffer.Length; i++)
            {
                if(damageBuffer[i].Hierarchy == DamageHierarchy.Main)
                {
                    var blockAmount = vanguard.ValueRO.BlockAmount;
                    ref var instance = ref damageBuffer.ElementAt(i);
                    instance.Damage = math.max(0, instance.Damage - blockAmount);
                }
            }
        }
    }
}
