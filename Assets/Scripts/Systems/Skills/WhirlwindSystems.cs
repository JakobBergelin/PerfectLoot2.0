using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.VisualScripting;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial class WhirlwindSystem : SystemBase
{
    private Unity.Mathematics.Random _random;

    private BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;
    [BurstCompile]
    protected override void OnCreate()
    {
        _random = Unity.Mathematics.Random.CreateFromIndex((uint)System.Environment.TickCount);
        _beginInitializationEntityCommandBufferSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
    }

    [BurstCompile]
    protected override void OnUpdate()
    {
        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
        foreach (var (whirlwind, entity) in SystemAPI.Query<RefRW<Whirlwind>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            if(!SystemAPI.IsComponentEnabled<Whirlwind>(entity) || !whirlwind.ValueRO.Spinning)
            {
                whirlwind.ValueRW.Spinning = false;
                if(SystemAPI.HasComponent<DisableTurning>(entity))
                {
                    commandBuffer.RemoveComponent<DisableTurning>(entity);
                }
                if(SystemAPI.HasComponent<Channeling>(entity))
                {
                    commandBuffer.RemoveComponent<Channeling>(entity);
                }
                if(whirlwind.ValueRO.WeaponEntity != Entity.Null)
                {
                    commandBuffer.AddComponent<DestroyWithChildren>(whirlwind.ValueRO.WeaponEntity);
                    whirlwind.ValueRW.WeaponEntity = Entity.Null;
                }
                whirlwind.ValueRW.Elapsed = 0.0f;
            }
            else if (whirlwind.ValueRO.Spinning)
            {
                whirlwind.ValueRW.Elapsed += SystemAPI.Time.DeltaTime;
            }
            
        }

        foreach (var (whirlwind, localTransform, resource, stats, entity) in SystemAPI.Query<RefRW<Whirlwind>, RefRW<LocalTransform>, RefRO<Resource>, RefRO<CalculatedStats>>().WithEntityAccess())
        {
            var resourceCost = whirlwind.ValueRO.ResourceCostPerSecond * SystemAPI.Time.DeltaTime;
            if(resource.ValueRO.CurrentValue >= resourceCost)
            {
                whirlwind.ValueRW.Spinning = true;
                if(whirlwind.ValueRO.Elapsed > whirlwind.ValueRO.SpinUpTime)
                {
                    if(whirlwind.ValueRO.WeaponEntity == Entity.Null)
                    {
                        whirlwind.ValueRW.WeaponEntity = EntityManager.CreateEntity();

                        var weaponDamage = stats.ValueRO.Value.WeaponDamage + _random.NextFloat(-stats.ValueRO.Value.WeaponDamageRange, stats.ValueRO.Value.WeaponDamageRange);
                        var damage = weaponDamage * whirlwind.ValueRO.WeaponDamageMultiplier;

                        Debug.Log(damage);

                        commandBuffer.AddComponent(whirlwind.ValueRW.WeaponEntity, new LocalTransform{Position = localTransform.ValueRO.Position, Rotation = Quaternion.identity, Scale = 1f});
                        commandBuffer.AddComponent(whirlwind.ValueRW.WeaponEntity, new Attack{Damage = damage, Type = DamageType.Physical, DamageHierarchy = DamageHierarchy.Main, Radius = whirlwind.ValueRO.AttackRadius});
                        commandBuffer.AddComponent(whirlwind.ValueRW.WeaponEntity, new Source{Entity = entity});
                        commandBuffer.AddComponent(whirlwind.ValueRW.WeaponEntity, new AttackTarget{TargetType = TargetType.Enemy});
                        commandBuffer.AddComponent(whirlwind.ValueRW.WeaponEntity, new InitializeFromSource{});

                    }
                    else
                    {
                        var weaponTransform = SystemAPI.GetComponentRW<LocalTransform>(whirlwind.ValueRO.WeaponEntity);
                        var forward = math.mul(localTransform.ValueRO.Rotation, Vector3.forward);
                        weaponTransform.ValueRW.Position = localTransform.ValueRO.Position + forward * stats.ValueRO.Value.WeaponReach;
                        SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger{Value = -resourceCost});
                    } 
                }
            }
            else
            {
                whirlwind.ValueRW.Spinning = false;
            }

            if (whirlwind.ValueRO.Spinning)
            {
                commandBuffer.AddComponent(entity, new DisableTurning());
                commandBuffer.AddComponent(entity, new Channeling());
                localTransform.ValueRW.Rotation = math.mul(localTransform.ValueRW.Rotation, quaternion.RotateY(whirlwind.ValueRO.RotationSpeed * SystemAPI.Time.DeltaTime * stats.ValueRO.Value.AttackSpeed));
            }

        }

        // Report spinning as active (after SkillActivationSystem's defaults)
        foreach (var (whirlwind, skillBuffer) in SystemAPI.Query<RefRO<Whirlwind>, DynamicBuffer<Skill>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            int index = -1;
            if (SkillUtil.FindSkill(skillBuffer, SkillType.Whirlwind, ref index))
            {
                skillBuffer.ElementAt(index).State.IsActive = whirlwind.ValueRO.Spinning;
            }
        }
    }
}
