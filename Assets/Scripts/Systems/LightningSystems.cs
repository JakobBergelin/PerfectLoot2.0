using Unity.Burst;
using Unity.Entities;
using UnityEngine;
using Unity.Transforms;

/// <summary>Charged (lightning hits): releases the charge as a lightning attack around the unit. Instant, so not an effect.</summary>
[BurstCompile]
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(EffectSystemGroup))]
public partial class LightningSystem : SystemBase
{
    private BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;

    [BurstCompile]
    protected override void OnCreate()
    {
        _beginInitializationEntityCommandBufferSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
    }

    [BurstCompile]
    protected override void OnUpdate()
    {
        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();

        foreach (var (instances, localTransform, entity) in SystemAPI.Query<DynamicBuffer<ChargedInstance>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            for(int i = 0; i < instances.Length; i++)
            {
                var newEntity = commandBuffer.CreateEntity();
                
                commandBuffer.AddComponent(newEntity, new LocalTransform{Position = localTransform.ValueRO.Position, Rotation = Quaternion.identity, Scale = 1f});
                commandBuffer.AddComponent(newEntity, new Attack{Damage = instances[i].Damage, Type = DamageType.Lightning, Radius = instances[i].Radius, DamageHierarchy = DamageHierarchy.Sub});
                commandBuffer.AddComponent(newEntity, new DestroyWithChildren{});
                // TODO source entity
                commandBuffer.AddComponent(newEntity, new Source{Entity = Entity.Null});
                // TODO source
                if(SystemAPI.HasComponent<Enemy>(entity))
                {
                    commandBuffer.AddComponent(newEntity, new AttackTarget{TargetType = TargetType.Enemy});
                }
                
            }
            instances.Clear();
        }
        
    }
}
