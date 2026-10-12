//using System;
//using Unity.Burst;
//using Unity.Entities;
//using UnityEngine;

//public struct NormalDagger : IComponentData
//{
//    public float WeaponDamage;  

//    public float CriticalDamage; 
//}

//[BurstCompile]
//[UpdateInGroup(typeof(CalculateStatsSystemGroup))]
//public partial struct CalculateNormalDaggerSystem : ISystem
//{
//    private const float DamageRange = 2.0f;
//    private const float AttackSpeed = 1.3f;


//    [BurstCompile]
//    public void OnUpdate(ref SystemState state)
//    {
//        foreach (var (dagger, stats, precentages) in SystemAPI.Query<RefRO<NormalDagger>, RefRW<CalculatedStats>, RefRW<CalculatedStatsPercentages>>())
//        {
//            stats.ValueRW.WeaponDamage += dagger.ValueRO.WeaponDamage;
//            stats.ValueRW.WeaponDamageRange += DamageRange;
//            stats.ValueRW.AttackSpeed *= AttackSpeed;

//            precentages.ValueRW.CriticalDamage += dagger.ValueRO.CriticalDamage;
//        }
//    }
//}

//[BurstCompile]
//[UpdateAfter(typeof(InventorySystem))]
//public partial struct RandomizeNormalDaggerSystem : ISystem
//{
//    [BurstCompile]
//    public void OnUpdate(ref SystemState state)
//    {
//        foreach (var (itemBuffer, normalDagger) in SystemAPI.Query<DynamicBuffer<InventoryItem>, RefRW<NormalDagger>>())
//        {
//            Guid seed = new Guid();
//            foreach(var item in itemBuffer)
//            {
//                if(item.Type == ItemType.NormalDagger)
//                {
//                    seed = item.Seed;
//                    break;
//                }
//            }
//            UnityEngine.Random.InitState(seed.GetHashCode());
//            var damageMax = Mathf.Lerp(Constants.Generate.Dagger.MinDamage, Constants.Generate.Dagger.MaxDamage, Constants.Generate.TierFactors.NormalMax);
//            var damageMin = Mathf.Lerp(Constants.Generate.Dagger.MinDamage, Constants.Generate.Dagger.MaxDamage, Constants.Generate.TierFactors.NormalMin);
//            normalDagger.ValueRW.WeaponDamage = UnityEngine.Random.Range(damageMin, damageMax);

//            var critMax = Mathf.Lerp(Constants.Generate.Dagger.MinCrit, Constants.Generate.Dagger.MaxCrit, Constants.Generate.TierFactors.NormalMax);
//            var critMin = Mathf.Lerp(Constants.Generate.Dagger.MinCrit, Constants.Generate.Dagger.MaxCrit, Constants.Generate.TierFactors.NormalMin);
//            normalDagger.ValueRW.CriticalDamage = UnityEngine.Random.Range(critMin, critMax);
//        }
//    }
//}
