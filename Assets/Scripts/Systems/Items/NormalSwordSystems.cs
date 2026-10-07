using System;
using Unity.Burst;
using Unity.Entities;
using UnityEngine;

public struct NormalSword : IComponentData
{
    public float WeaponDamage;   

    // TODO find what swords do
}

[BurstCompile]
[UpdateInGroup(typeof(CalculateStatsSystemGroup))]
public partial struct CalculateNormalSwordSystem : ISystem
{
    private const float DamageRange = 1.0f;
    private const float AttackSpeed = 1.0f;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (sword, stats) in SystemAPI.Query<RefRO<NormalSword>, RefRW<CalculatedStats>>())
        {
            stats.ValueRW.WeaponDamage += sword.ValueRO.WeaponDamage;
            stats.ValueRW.WeaponDamageRange += DamageRange;
            stats.ValueRW.AttackSpeed *= AttackSpeed;
        }
    }
}

[BurstCompile]
[UpdateAfter(typeof(InventorySystem))]
public partial struct RandomizeNormalSwordSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (itemBuffer, normalSword) in SystemAPI.Query<DynamicBuffer<InventoryItem>, RefRW<NormalSword>>())
        {
            Guid seed = new Guid();
            foreach(var item in itemBuffer)
            {
                if(item.Type == ItemType.NormalSword)
                {
                    seed = item.Seed;
                    break;
                }
            }
            UnityEngine.Random.InitState(seed.GetHashCode());
            var damageMax = Mathf.Lerp(Constants.Generate.Sword.MinDamage, Constants.Generate.Sword.MaxDamage, Constants.Generate.TierFactors.NormalMax);
            var damageMin = Mathf.Lerp(Constants.Generate.Sword.MinDamage, Constants.Generate.Sword.MaxDamage, Constants.Generate.TierFactors.NormalMin);
            normalSword.ValueRW.WeaponDamage = UnityEngine.Random.Range(damageMin, damageMax);
        }
    }
}
