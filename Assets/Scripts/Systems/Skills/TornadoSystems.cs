using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.SocialPlatforms;

[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial class TornadoSystem : SystemBase
{
    private const float ResourceCost = 15.0f;
    private const float AttackSpeed = 0.9f;
    private const float Radius = 2f;
    private const float TornadoDirectionDuration = 0.7f;
    private const float TornadoSpeed = 5f;
    private const float WeaponDamagePerSecond = 0.2f;
    private const float DamagePerSecond = 0.5f;

    private const float TickRate = 0.5f;
    private BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;
    [BurstCompile]
    protected override void OnCreate()
    {
        _beginInitializationEntityCommandBufferSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
    }

    [BurstCompile]
    protected override void OnUpdate()
    {
        foreach (var skillBuffer in SystemAPI.Query<DynamicBuffer<Skill>>().WithPresent<Tornado>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            int index = -1;
            if (SkillUtil.FindSkill(skillBuffer, SkillType.Tornado, ref index))
            {
                ref var skill = ref skillBuffer.ElementAt(index);

                skill.UIInformation.MaxCooldown = 0.0f;
                skill.UIInformation.CurrentCooldown = 0.0f;
                skill.UIInformation.ResourceCost = 0.0f;
                skill.UIInformation.ResourceCostPerSecond = 0.0f;
            }
        }

        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
        foreach (var (tornado,stats, entity) in SystemAPI.Query<RefRW<Tornado>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            tornado.ValueRW.Elapsed += SystemAPI.Time.DeltaTime * stats.ValueRO.Value.AttackSpeed;
            tornado.ValueRW.Elapsed = Mathf.Min(tornado.ValueRW.Elapsed, AttackSpeed);
        }

        foreach (var (tornado, localTransform, desiredDirection, resource,stats, entity) in SystemAPI.Query<RefRW<Tornado>, RefRW<LocalTransform>, RefRO<DesiredDirection>, RefRO<Resource>, RefRO<CalculatedStats>>().WithEntityAccess())
        {
            localTransform.ValueRW.Rotation = Quaternion.RotateTowards(localTransform.ValueRO.Rotation, desiredDirection.ValueRO.Quaternion, Constants.RotationSpeed * SystemAPI.Time.DeltaTime);

            if(Quaternion.Angle(localTransform.ValueRW.Rotation, desiredDirection.ValueRO.Quaternion) > (math.PI / 2))
            {
                continue;
            }

            if(resource.ValueRO.CurrentValue < ResourceCost)
            {
                tornado.ValueRW.Elapsed = 0.0f;
                continue;
            }

            if(tornado.ValueRO.Elapsed >= AttackSpeed)
            {
                var newEntity = commandBuffer.CreateEntity();
                
                var startPos = localTransform.ValueRO.Position + desiredDirection.ValueRO.Direction;
                UnityEngine.Random.InitState(System.DateTime.Now.Millisecond);
                var weaponDamage = stats.ValueRO.Value.WeaponDamage + UnityEngine.Random.Range(-stats.ValueRO.Value.WeaponDamageRange, stats.ValueRO.Value.WeaponDamageRange);
                var damage = weaponDamage * WeaponDamagePerSecond + DamagePerSecond;
                commandBuffer.AddComponent(newEntity, new LocalTransform{Position = startPos, Rotation = Quaternion.identity, Scale = 1f});
                commandBuffer.AddComponent(newEntity, new PeriodicAttack{DamagePerSecond = damage, Type = DamageType.Poison, Radius = Radius, TimeRemaining = 4f, Skill= SkillType.Tornado, TickRate = TickRate});
                commandBuffer.AddComponent(newEntity, new Source{Entity = entity});
                commandBuffer.AddComponent(newEntity, new AttackTarget{TargetType = TargetType.Enemy});
                commandBuffer.AddComponent(newEntity, new TornadoBehavior{Elapsed = 0.0f, Direction = desiredDirection.ValueRO.Direction, Position = startPos, Sway = new float3(1.0f,0.0f,0.0f)});
                commandBuffer.AddComponent(newEntity, new InitializeFromSource{});

                tornado.ValueRW.Elapsed = 0.0f;

                SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger{Value = -ResourceCost});
            }
        }

        foreach (var (tornado, localTransform, entity) in SystemAPI.Query<RefRW<TornadoBehavior>, RefRW<LocalTransform>>().WithEntityAccess())
        {
            tornado.ValueRW.Elapsed += SystemAPI.Time.DeltaTime;
            if(tornado.ValueRO.Elapsed >= TornadoDirectionDuration)
            {
                tornado.ValueRW.Elapsed = 0.0f;
                UnityEngine.Random.InitState(System.DateTime.Now.Millisecond);
                tornado.ValueRW.Direction = new float3(UnityEngine.Random.Range(-1.0f, 1.0f), 0.0f, UnityEngine.Random.Range(-1.0f, 1.0f));
            }
            
            tornado.ValueRW.Sway = Quaternion.Euler(0.0f, (float)Math.PI * 2.0f, 0.0f) * tornado.ValueRO.Sway;
            tornado.ValueRW.Position += tornado.ValueRO.Direction * TornadoSpeed * SystemAPI.Time.DeltaTime;
            localTransform.ValueRW.Position = tornado.ValueRO.Position + math.normalize(tornado.ValueRO.Sway) * 0.3f;
        }

    }
}
