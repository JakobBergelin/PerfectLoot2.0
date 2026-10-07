using Unity.Burst;
using Unity.Entities;
using Unity.Physics;
using Unity.Collections;
using Unity.Transforms;
using UnityEngine;
using System;

public struct Heal : IComponentData
{
    public float Value;
    public float Radius;
    public TargetType Type;
}

[BurstCompile]
public partial class HealingSystem : SystemBase
{
    private EndSimulationEntityCommandBufferSystem _endSimulationEntityCommandBufferSystem;

    [BurstCompile]
    protected override void OnCreate()
    {
        _endSimulationEntityCommandBufferSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<EndSimulationEntityCommandBufferSystem>();
    }

    [BurstCompile]
    protected override void OnUpdate()
    {
        var physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        NativeList<DistanceHit> outHits = new NativeList<DistanceHit>(Allocator.Temp);
        // TODO only group with hitable entities
        var collisionFilter = new CollisionFilter
        {
            BelongsTo = ~0u,
            CollidesWith = (uint)CollisionLayerHelper.Enemy | (uint)CollisionLayerHelper.Player,
            GroupIndex = 0,
        };

        var commandBuffer = _endSimulationEntityCommandBufferSystem.CreateCommandBuffer();

        foreach (var (heal, localTransform, source, entity) in SystemAPI.Query<RefRW<Heal>, RefRO<LocalTransform>, RefRO<Source>>().WithEntityAccess())
        {
            outHits.Clear();
            physicsWorldSingleton.OverlapSphere(localTransform.ValueRO.Position, heal.ValueRO.Radius, ref outHits, collisionFilter);
            if(!outHits.IsEmpty)
            {
                foreach(var hit in outHits)
                {
                    if(hit.Entity != entity)
                    {
                        if(heal.ValueRO.Type == TargetType.Player && !SystemAPI.HasComponent<Player>(hit.Entity))
                        {
                            continue;
                        }
                        else if(heal.ValueRO.Type == TargetType.Enemy && !SystemAPI.HasComponent<Enemy>(hit.Entity))
                        {
                            continue;
                        }
                        if(SystemAPI.HasComponent<Health>(hit.Entity))
                        {
                            commandBuffer.AppendToBuffer(hit.Entity, new HealingInstance{Value = heal.ValueRO.Value, Source = source.ValueRO.Entity});
                        }
                    }
                }
            }
        }
    }
}

[BurstCompile]
public partial class PeriodicHealSystem : SystemBase
{
    private EndSimulationEntityCommandBufferSystem _endSimulationEntityCommandBufferSystem;
    [BurstCompile]
    protected override void OnCreate()
    {
        //_endSimulationEntityCommandBufferSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<EndSimulationEntityCommandBufferSystem>();
    }
    [BurstCompile]
    protected override void OnUpdate()
    {
        // Projectile movement and hit detection
        // var physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        // NativeList<DistanceHit> outHits = new NativeList<DistanceHit>(Allocator.Temp);
        // // TODO only group with hitable entities
        // var collisionFilter = new CollisionFilter
        // {
        //     BelongsTo = ~0u,
        //     CollidesWith = (uint)CollisionLayerHelper.Enemy,
        //     GroupIndex = 0,
        // };


        // foreach (var (localTransform, periodicAttack, source, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<PeriodicAttack>, RefRO<Source>>().WithEntityAccess())
        // {
        //     outHits.Clear();
        //     physicsWorldSingleton.OverlapSphere(localTransform.ValueRO.Position, periodicAttack.ValueRO.Radius, ref outHits, collisionFilter);
        //     if(!outHits.IsEmpty)
        //     {
        //         foreach(var hit in outHits)
        //         {
        //             if(hit.Entity != entity)
        //             {
        //                 if (source.ValueRO.Entity == hit.Entity)
        //                 {
        //                     continue;
        //                 }
        //                 if (SystemAPI.HasComponent<AttackTarget>(entity))
        //                 {
        //                     var targetType = SystemAPI.GetComponentRO<AttackTarget>(entity).ValueRO.TargetType;
        //                     if(targetType == TargetType.Player && !SystemAPI.HasComponent<Player>(hit.Entity))
        //                     {
        //                         continue;
        //                     }
        //                     else if(targetType == TargetType.Enemy && !SystemAPI.HasComponent<Enemy>(hit.Entity))
        //                     {
        //                         continue;
        //                     }
        //                 }
        //                 if(SystemAPI.HasComponent<Health>(hit.Entity))
        //                 {
        //                     DynamicBuffer<PeriodicDamageInstance> periodicDamageBuffer = SystemAPI.GetBuffer<PeriodicDamageInstance>(hit.Entity);;
        //                     int index = -1;
        //                     for(int i = 0; i < periodicDamageBuffer.Length; i++)
        //                     {
        //                         if(periodicDamageBuffer[i].Skill == periodicAttack.ValueRO.Skill)
        //                         {
        //                             index = i;
        //                             break;
        //                         }
        //                     }
        //                     float damage = periodicAttack.ValueRO.DamagePerSecond * SystemAPI.Time.DeltaTime;
        //                     if(index == -1)
        //                     {
        //                         periodicDamageBuffer.Add(new PeriodicDamageInstance{AccumilatedDamage = damage, Type = periodicAttack.ValueRO.Type, Hierarchy = DamageHierarchy.Main, Skill = periodicAttack.ValueRO.Skill, Tick = periodicAttack.ValueRO.TickRate, Source = source.ValueRO.Entity});
        //                     }
        //                     else
        //                     {
        //                         ref var instance = ref periodicDamageBuffer.ElementAt(index);
        //                         instance.AccumilatedDamage += damage;
        //                     }
                            
        //                     break;
        //                 }
                        
        //             }
        //         }
        //     }
        // }

        // NativeList<Entity> toDestroy = new NativeList<Entity>(Allocator.Temp);
        // foreach (var (periodicAttack, entity) in SystemAPI.Query<RefRW<PeriodicAttack>>().WithEntityAccess())
        // {
        //     periodicAttack.ValueRW.TimeRemaining -= SystemAPI.Time.DeltaTime;
        //     if(periodicAttack.ValueRO.TimeRemaining <= 0.0f)
        //     {
        //         toDestroy.Add(entity);
        //     }
        // }

        // if(!toDestroy.IsEmpty)
        // {
        //     var commandBuffer = _endSimulationEntityCommandBufferSystem.CreateCommandBuffer();
        //     commandBuffer.AddComponent<DestroyWithChildren>(toDestroy.AsArray());
        // }
    }
}

[BurstCompile]
public partial class AccumilatedPeriodicHealSystem : SystemBase
{
    [BurstCompile]
    protected override void OnUpdate()
    {
        // foreach (var (periodicDamageInstance, DamageInstance, entity) in SystemAPI.Query<DynamicBuffer<PeriodicDamageInstance>, DynamicBuffer<DamageInstance>>().WithEntityAccess())
        // {
        //     for(int i = 0; i < periodicDamageInstance.Length; i++)
        //     {
        //         ref var instance = ref periodicDamageInstance.ElementAt(i);
        //         instance.Tick -= SystemAPI.Time.DeltaTime;

        //         if(instance.Tick <= 0.0f)
        //         {
        //             bool isCrit = false;
        //             var damage = instance.AccumilatedDamage;

        //             AttackSystem.CalculateCrit(instance.Source, EntityManager, ref damage, ref isCrit);

        //             DamageInstance.Add(new DamageInstance{Damage = damage, Type = instance.Type, Hierarchy = instance.Hierarchy, Source = instance.Source, IsCrit = isCrit});
        //             periodicDamageInstance.RemoveAt(i);
        //             i--;
        //         }
        //     }
        // }
    }
}
