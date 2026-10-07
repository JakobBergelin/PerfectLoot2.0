using Unity.Burst;
using Unity.Entities;
using UnityEngine;
using Unity.Physics;
using Unity.Collections;
using Unity.Transforms;


[UpdateInGroup(typeof(SubDamageSystemGroup))]
partial struct PhysicalSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (instances, health, entity) in SystemAPI.Query<DynamicBuffer<BleedInstance>, RefRO<Health>>().WithEntityAccess())
        {
            SystemAPI.SetComponentEnabled<Bleeding>(entity, !instances.IsEmpty);

            for(int i = 0; i < instances.Length; i++)
            {
                float damage = health.ValueRO.CurrentValue * Constants.DefaultBleedDamageHealthMultiplier * Time.deltaTime;
                SystemAPI.GetBuffer<DamageInstance>(entity).Add(new DamageInstance{Damage = damage, Type = DamageType.Physical, Hierarchy = DamageHierarchy.Sub, IsCrit = false});
                ref var instance = ref instances.ElementAt(i);
                instance.TimeRemaining -= Time.deltaTime;
                var bleeding = SystemAPI.GetComponentRW<Bleeding>(entity);
                bleeding.ValueRW.DamageToDisplay += damage;
                if(instance.TimeRemaining <= 0)
                {
                    instances.RemoveAt(i);
                    i--;
                }
            }
        }
    }
}

[UpdateInGroup(typeof(SubDamageSystemGroup))]
partial struct ColdSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (chilled,entity) in SystemAPI.Query<RefRW<Chilled>>().WithEntityAccess())
        {
            if(chilled.ValueRO.Value >= 100.0f)
            {
                SystemAPI.SetComponentEnabled<Frozen>(entity, true);
                SystemAPI.GetComponentRW<Frozen>(entity).ValueRW.TimeRemaining = Constants.DefaultFrozenTime;
                chilled.ValueRW.Value = 0;
                SystemAPI.SetComponentEnabled<Chilled>(entity, false);
            }
        }

        foreach (var (frozen,entity) in SystemAPI.Query<RefRW<Frozen>>().WithEntityAccess())
        {
            frozen.ValueRW.TimeRemaining -= Time.deltaTime;
            if(frozen.ValueRO.TimeRemaining <= 0)
            {
                SystemAPI.SetComponentEnabled<Frozen>(entity, false);
            }
        }
    }
}

[UpdateInGroup(typeof(SubDamageSystemGroup))]
partial struct FireSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (instances, entity) in SystemAPI.Query<DynamicBuffer<BurnInstance>>().WithEntityAccess())
        {
            SystemAPI.SetComponentEnabled<Burning>(entity, !instances.IsEmpty);

            for(int i = 0; i < instances.Length; i++)
            {
                float damage = instances[i].DamagePerSecond * Time.deltaTime;
                SystemAPI.GetBuffer<DamageInstance>(entity).Add(new DamageInstance{Damage = damage, Type = DamageType.Fire, Hierarchy = DamageHierarchy.Sub, IsCrit = false});
                ref var instance = ref instances.ElementAt(i);
                instance.TimeRemaining -= Time.deltaTime;
                var burning = SystemAPI.GetComponentRW<Burning>(entity);
                burning.ValueRW.DamageToDisplay += damage;
                if(instance.TimeRemaining <= 0)
                {
                    instances.RemoveAt(i);
                    i--;
                }
            }
        }
    }
}

[UpdateInGroup(typeof(SubDamageSystemGroup))]
partial struct PoisonSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (poisoned,entity) in SystemAPI.Query<RefRW<Poisioned>>().WithEntityAccess())
        {
            poisoned.ValueRW.TimeRemaining -= Time.deltaTime;
            if(poisoned.ValueRO.TimeRemaining >= 0)
            {
                float damage = Constants.DefaultPoisonDamagePerSecond * Time.deltaTime;
                SystemAPI.GetBuffer<DamageInstance>(entity).Add(new DamageInstance{Damage = damage, Type = DamageType.Poison, Hierarchy = DamageHierarchy.Sub, IsCrit = false});

            }
            else
            {
                SystemAPI.SetComponentEnabled<Poisioned>(entity ,false);
            }
        }
    }
}

[BurstCompile]
[UpdateInGroup(typeof(SubDamageSystemGroup))]
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
