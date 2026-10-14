using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

public struct Luminosity : IComponentData
{
    public float HealValue;
    public float HealRadius;
}

[BurstCompile]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateBefore(typeof(DamagingSystem))]
public partial class LuminositySystem : SystemBase
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
        foreach (var (calculatedDamageBuffer, localTransform, entity) in SystemAPI.Query<DynamicBuffer<CalculatedDamageInstance>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            foreach(var instance in calculatedDamageBuffer)
            {
                var source = instance.Source;
                if(SystemAPI.HasComponent<Luminosity>(source))
                {
                    if(instance.IsCrit)
                    {
                        var luminosity = SystemAPI.GetComponent<Luminosity>(source);
                        var newEntity = commandBuffer.CreateEntity();
                
                        UnityEngine.Random.InitState(System.DateTime.Now.Millisecond);
                        commandBuffer.AddComponent(newEntity, new LocalTransform{Position = localTransform.ValueRO.Position, Rotation = Quaternion.identity, Scale = 1f});
                        commandBuffer.AddComponent(newEntity, new Heal{Value = luminosity.HealValue, Type = TargetType.Player, Radius = luminosity.HealRadius});
                        commandBuffer.AddComponent(newEntity, new Source{Entity = instance.Source});
                        commandBuffer.AddComponent(newEntity, new InitializeFromSource{});
                        // single frame heal
                        commandBuffer.AddComponent(newEntity, new DestroyWithChildren{});
                    }
                }
            }
        }
    }
}
