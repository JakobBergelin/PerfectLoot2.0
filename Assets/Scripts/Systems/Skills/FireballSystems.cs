using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial class FireballSystem : SystemBase
{
    private const float Damage = 2.0f;
    private BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;
    [BurstCompile]
    protected override void OnCreate()
    {
        _beginInitializationEntityCommandBufferSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
    }

    [BurstCompile]
    protected override void OnUpdate()
    {
        // SkillState: no cooldown to report (Elapsed is a wind-up that restarts when released); IsActive/resource set by SkillActivationSystem
        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
        foreach (var (fireball, stats, entity) in SystemAPI.Query<RefRW<FireBall>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            if(!SystemAPI.IsComponentEnabled<FireBall>(entity))
            {
                fireball.ValueRW.Elapsed = 0;
            }
            fireball.ValueRW.Elapsed += SystemAPI.Time.DeltaTime * stats.ValueRO.Value.AttackSpeed;
            fireball.ValueRW.Elapsed = Mathf.Min(fireball.ValueRW.Elapsed, fireball.ValueRW.AttackSpeed);
        }

        foreach (var (fireball, skillBuffer, localTransform, desiredDirection, resource, entity) in SystemAPI.Query<RefRW<FireBall>, DynamicBuffer<Skill>, RefRW<LocalTransform>, RefRO<DesiredDirection>, RefRO<Resource>>().WithEntityAccess())
        {
            localTransform.ValueRW.Rotation = Quaternion.RotateTowards(localTransform.ValueRO.Rotation, desiredDirection.ValueRO.Quaternion, Constants.RotationSpeed * SystemAPI.Time.DeltaTime);
            if(resource.ValueRO.CurrentValue < fireball.ValueRO.ResourceCost)
            {
                fireball.ValueRW.Elapsed = 0;
                continue;
            }

            if(Quaternion.Angle(localTransform.ValueRW.Rotation, desiredDirection.ValueRO.Quaternion) > (math.PI / 2))
            {
                continue;
            }

            if(fireball.ValueRO.Elapsed >= fireball.ValueRO.AttackSpeed)
            {
                var newEntity = commandBuffer.CreateEntity();
                
                commandBuffer.AddComponent(newEntity, new LocalTransform{Position = localTransform.ValueRO.Position + desiredDirection.ValueRO.Direction, Rotation = Quaternion.identity, Scale = 0.5f});
                commandBuffer.AddComponent(newEntity, new Projectile{Direction = desiredDirection.ValueRO.Direction, Speed = 15.0f, DestroyOnHit = true, BounceCount = 0, PierceCount = 0});
                commandBuffer.AddComponent(newEntity, new Attack{Damage = Damage, Type = DamageType.Fire, DamageHierarchy = DamageHierarchy.Main, Radius = 0.25f});
                commandBuffer.AddComponent(newEntity, new Source{Entity = entity});
                commandBuffer.AddComponent(newEntity, new AttackTarget{TargetType = TargetType.Enemy});
                commandBuffer.AddComponent(newEntity, new InitializeFromSource{});

                fireball.ValueRW.Elapsed = 0.0f;

                SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger{Value = -fireball.ValueRO.ResourceCost});

                int index = -1;
                if (SkillUtil.FindSkill(skillBuffer, SkillType.Fireball, ref index))
                {
                    ref var skill = ref skillBuffer.ElementAt(index);
                    skill.events.OnUsed = true;
                }
            }
        }
    }
}
