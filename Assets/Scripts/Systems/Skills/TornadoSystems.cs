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
    private Unity.Mathematics.Random _random;

    private BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;
    [BurstCompile]
    protected override void OnCreate()
    {
        _random = Unity.Mathematics.Random.CreateFromIndex((uint)System.Environment.TickCount);
        _beginInitializationEntityCommandBufferSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
        RequireForUpdate<MovementConfig>();
    }

    [BurstCompile]
    protected override void OnUpdate()
    {
        // SkillState: no cooldown to report (Elapsed is a wind-up); IsActive/resource set by SkillActivationSystem
        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
        var movementConfig = SystemAPI.GetSingleton<MovementConfig>();
        foreach (var (tornado,stats, entity) in SystemAPI.Query<RefRW<Tornado>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            tornado.ValueRW.Elapsed += SystemAPI.Time.DeltaTime * stats.ValueRO.Value.AttackSpeed;
            tornado.ValueRW.Elapsed = Mathf.Min(tornado.ValueRW.Elapsed, tornado.ValueRO.AttackSpeed);
        }

        foreach (var (tornado, localTransform, desiredDirection, resource,stats, entity) in SystemAPI.Query<RefRW<Tornado>, RefRW<LocalTransform>, RefRO<DesiredDirection>, RefRO<Resource>, RefRO<CalculatedStats>>().WithEntityAccess())
        {
            localTransform.ValueRW.Rotation = Quaternion.RotateTowards(localTransform.ValueRO.Rotation, desiredDirection.ValueRO.Quaternion, movementConfig.RotationSpeed * SystemAPI.Time.DeltaTime);

            if(Quaternion.Angle(localTransform.ValueRW.Rotation, desiredDirection.ValueRO.Quaternion) > (math.PI / 2))
            {
                continue;
            }

            if(resource.ValueRO.CurrentValue < tornado.ValueRO.ResourceCost)
            {
                tornado.ValueRW.Elapsed = 0.0f;
                continue;
            }

            if(tornado.ValueRO.Elapsed >= tornado.ValueRO.AttackSpeed)
            {
                var newEntity = commandBuffer.CreateEntity();
                
                var startPos = localTransform.ValueRO.Position + desiredDirection.ValueRO.Direction;
                var weaponDamage = stats.ValueRO.Value.WeaponDamage + _random.NextFloat(-stats.ValueRO.Value.WeaponDamageRange, stats.ValueRO.Value.WeaponDamageRange);
                var damage = weaponDamage * tornado.ValueRO.WeaponDamagePerSecond + tornado.ValueRO.DamagePerSecond;
                commandBuffer.AddComponent(newEntity, new LocalTransform{Position = startPos, Rotation = Quaternion.identity, Scale = 1f});
                commandBuffer.AddComponent(newEntity, new PeriodicAttack{DamagePerSecond = damage, Type = DamageType.Poison, Radius = tornado.ValueRO.Radius, TimeRemaining = tornado.ValueRO.Lifetime, Skill= SkillType.Tornado, TickRate = tornado.ValueRO.TickRate});
                commandBuffer.AddComponent(newEntity, new Source{Entity = entity});
                commandBuffer.AddComponent(newEntity, new AttackTarget{TargetType = TargetType.Enemy});
                commandBuffer.AddComponent(newEntity, new TornadoBehavior{DirectionDuration = tornado.ValueRO.DirectionDuration, Speed = tornado.ValueRO.Speed, Elapsed = 0.0f, Direction = desiredDirection.ValueRO.Direction, Position = startPos, Sway = new float3(1.0f,0.0f,0.0f)});
                commandBuffer.AddComponent(newEntity, new InitializeFromSource{});

                tornado.ValueRW.Elapsed = 0.0f;

                SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger{Value = -tornado.ValueRO.ResourceCost});
            }
        }

        foreach (var (tornado, localTransform, entity) in SystemAPI.Query<RefRW<TornadoBehavior>, RefRW<LocalTransform>>().WithEntityAccess())
        {
            tornado.ValueRW.Elapsed += SystemAPI.Time.DeltaTime;
            if(tornado.ValueRO.Elapsed >= tornado.ValueRO.DirectionDuration)
            {
                tornado.ValueRW.Elapsed = 0.0f;
                tornado.ValueRW.Direction = new float3(_random.NextFloat(-1.0f, 1.0f), 0.0f, _random.NextFloat(-1.0f, 1.0f));
            }
            
            tornado.ValueRW.Sway = Quaternion.Euler(0.0f, (float)Math.PI * 2.0f, 0.0f) * tornado.ValueRO.Sway;
            tornado.ValueRW.Position += tornado.ValueRO.Direction * tornado.ValueRO.Speed * SystemAPI.Time.DeltaTime;
            localTransform.ValueRW.Position = tornado.ValueRO.Position + math.normalize(tornado.ValueRO.Sway) * 0.3f;
        }

    }
}
