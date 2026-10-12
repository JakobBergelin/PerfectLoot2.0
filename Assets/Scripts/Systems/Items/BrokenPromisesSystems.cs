//using System;
//using Unity.Burst;
//using Unity.Entities;
//using UnityEngine;

//public struct BrokenPromises : IComponentData
//{

//    // TODO ring affixes

//    public int NonCritHits; 
//}

//public struct BrokenPromisesActive : IComponentData
//{
//    public float TimeRemaining;
//}

//[BurstCompile]
//[UpdateInGroup(typeof(CalculateStatsSystemGroup))]
//public partial struct CalculateBrokenPromisesSystem : ISystem
//{

//    [BurstCompile]
//    public void OnUpdate(ref SystemState state)
//    {
//        foreach (var (item, stats, precentages) in SystemAPI.Query<RefRO<BrokenPromises>, RefRW<CalculatedStats>, RefRW<CalculatedStatsPercentages>>())
//        {

//        }
//    }
//}

//[BurstCompile]
//[UpdateInGroup(typeof(CalculateStatsSystemGroup))]
//public partial struct CalculateBrokenPromisesActiveSystem : ISystem
//{

//    [BurstCompile]
//    public void OnUpdate(ref SystemState state)
//    {
//        foreach (var (item, precentages) in SystemAPI.Query<RefRO<BrokenPromisesActive>, RefRW<CalculatedStatsPercentages>>())
//        {
//            precentages.ValueRW.CriticalChance += 1.0f;
//        }
//    }
//}

//[BurstCompile]
//public partial class BrokenPromisesActiveSystem : SystemBase
//{
//    private BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;
//    [BurstCompile]
//    protected override void OnCreate()
//    {
//        _beginInitializationEntityCommandBufferSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
//    }

//    [BurstCompile]
//    protected override void OnUpdate()
//    {
//        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
//        foreach (var (brokenPromises, entity) in SystemAPI.Query<RefRW<BrokenPromisesActive>>().WithEntityAccess())
//        {
//            brokenPromises.ValueRW.TimeRemaining -= SystemAPI.Time.DeltaTime;
//            if(brokenPromises.ValueRO.TimeRemaining <= 0.0f)
//            {
//                commandBuffer.RemoveComponent<BrokenPromisesActive>(entity);
//            }
//        }
//    }
//}

//[BurstCompile]
//[UpdateAfter(typeof(InventorySystem))]
//public partial struct RandomizeBrokenPromisesSystem : ISystem
//{
//    [BurstCompile]
//    public void OnUpdate(ref SystemState state)
//    {
//        foreach (var (itemBuffer, item) in SystemAPI.Query<DynamicBuffer<InventoryItem>, RefRW<BrokenPromises>>())
//        {
//            Guid seed = new Guid();
//            foreach(var i in itemBuffer)
//            {
//                if(i.Type == ItemType.BrokenPromises)
//                {
//                    seed = i.Seed;
//                    break;
//                }
//            }
//            UnityEngine.Random.InitState(seed.GetHashCode());
//        }
//    }
//}

//[BurstCompile]
//[UpdateAfter(typeof(CalculateDamageSystem))]
//[UpdateBefore(typeof(DamagingSystem))]
//public partial class BrokenPromisesSystem : SystemBase
//{
//    private BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;
//    [BurstCompile]
//    protected override void OnCreate()
//    {
//        _beginInitializationEntityCommandBufferSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
//    }

//    [BurstCompile]
//    protected override void OnUpdate()
//    {
//        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
//        foreach (var (calculatedDamageBuffer, entity) in SystemAPI.Query<DynamicBuffer<CalculatedDamageInstance>>().WithEntityAccess())
//        {
//            foreach(var instance in calculatedDamageBuffer)
//            {
//                var source = instance.Source;
//                if(SystemAPI.HasComponent<BrokenPromises>(source))
//                {
                    
//                    var brokenPromises = SystemAPI.GetComponentRW<BrokenPromises>(source);
//                    if(instance.IsCrit)
//                    {
//                        brokenPromises.ValueRW.NonCritHits = 0;
//                    }
//                    else
//                    {
//                        brokenPromises.ValueRW.NonCritHits++;
//                    }

//                    if(brokenPromises.ValueRO.NonCritHits >= 10)
//                    {
//                        brokenPromises.ValueRW.NonCritHits = 0;
//                        commandBuffer.AddComponent(source, new BrokenPromisesActive{TimeRemaining = 2.0f});
//                    }
//                }
//            }
//        }
//    }
//}
