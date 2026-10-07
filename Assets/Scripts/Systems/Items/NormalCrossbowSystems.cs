using System;
using Unity.Burst;
using Unity.Entities;
using UnityEngine;

public struct NormalCrossbow : IComponentData
{
    public float WeaponDamage;   

}

[BurstCompile]
[UpdateInGroup(typeof(CalculateStatsSystemGroup))]
public partial struct CalculateNormalCrossbowSystem : ISystem
{
    private const float DamageRange = 2.5f;
    private const float AttackSpeed = 0.8f;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (item, stats) in SystemAPI.Query<RefRO<NormalCrossbow>, RefRW<CalculatedStats>>())
        {
            stats.ValueRW.WeaponDamage += item.ValueRO.WeaponDamage;
            stats.ValueRW.WeaponDamageRange += DamageRange;
            stats.ValueRW.AttackSpeed *= AttackSpeed;
            stats.ValueRW.PierceOnCrit = true;
        }
    }
}

[BurstCompile]
[UpdateAfter(typeof(InventorySystem))]
public partial struct RandomizeNormalCrossbowSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (itemBuffer, normalCrossbow) in SystemAPI.Query<DynamicBuffer<InventoryItem>, RefRW<NormalCrossbow>>())
        {
            Guid seed = new Guid();
            foreach(var item in itemBuffer)
            {
                if(item.Type == ItemType.NormalCrossbow)
                {
                    seed = item.Seed;
                    break;
                }
            }
            UnityEngine.Random.InitState(seed.GetHashCode());
            var damageMax = Mathf.Lerp(Constants.Generate.Crossbow.MinDamage, Constants.Generate.Crossbow.MaxDamage, Constants.Generate.TierFactors.NormalMax);
            var damageMin = Mathf.Lerp(Constants.Generate.Crossbow.MinDamage, Constants.Generate.Crossbow.MaxDamage, Constants.Generate.TierFactors.NormalMin);
            normalCrossbow.ValueRW.WeaponDamage = UnityEngine.Random.Range(damageMin, damageMax);
        }
    }
}
