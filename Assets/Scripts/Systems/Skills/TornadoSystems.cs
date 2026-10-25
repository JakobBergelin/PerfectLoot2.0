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

        // Casts requested by effects (e.g. Echoing Blade): no wind-up, the normal cost (skipped if it can't be paid)
        foreach (var (tornado, requests, skills, resourceChanges, resource, localTransform, stats, entity) in
                 SystemAPI.Query<RefRO<Tornado>, DynamicBuffer<SkillCastRequest>, DynamicBuffer<Skill>, DynamicBuffer<ResourceChanger>, RefRO<Resource>, RefRO<LocalTransform>, RefRO<CalculatedStats>>()
                          .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            float available = resource.ValueRO.CurrentValue;
            while (SkillUtil.TryTakeRequest(requests, SkillType.Tornado, out var request))
            {
                if (available < tornado.ValueRO.ResourceCost) continue;
                available -= tornado.ValueRO.ResourceCost;
                Spawn(commandBuffer, entity, tornado.ValueRO, stats.ValueRO.Value, localTransform.ValueRO.Position, request.Direction, ref _random);
                resourceChanges.Add(new ResourceChanger{Value = -tornado.ValueRO.ResourceCost});
                SkillUtil.MarkUsed(skills, SkillType.Tornado, request.AutoCast);
            }
        }

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
                Spawn(commandBuffer, entity, tornado.ValueRO, stats.ValueRO.Value, localTransform.ValueRO.Position, desiredDirection.ValueRO.Direction, ref _random);
                tornado.ValueRW.Elapsed = 0.0f;

                SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger{Value = -tornado.ValueRO.ResourceCost});
                if(SystemAPI.HasBuffer<Skill>(entity)) SkillUtil.MarkUsed(SystemAPI.GetBuffer<Skill>(entity), SkillType.Tornado);
            }
        }
        // Spawned tornadoes move as wandering projectiles (WanderingProjectileSystem, ProjectileSystem)
    }

    /// <summary>
    /// One tornado from the caster toward direction. Used by the normal cast and by effects that cast it (Echoing Blade);
    /// the caller pays the cost.
    /// </summary>
    public static void Spawn(EntityCommandBuffer commandBuffer, Entity caster, in Tornado tornado, in StatBlock stats, float3 casterPosition, float3 direction, ref Unity.Mathematics.Random random)
    {
        var newEntity = commandBuffer.CreateEntity();

        var startPos = casterPosition + direction;
        var weaponDamage = stats.WeaponDamage + random.NextFloat(-stats.WeaponDamageRange, stats.WeaponDamageRange);
        var damage = weaponDamage * tornado.WeaponDamagePerSecond + tornado.DamagePerSecond;
        commandBuffer.AddComponent(newEntity, new LocalTransform{Position = startPos, Rotation = quaternion.identity, Scale = 1f});
        commandBuffer.AddComponent(newEntity, new PeriodicAttack{DamagePerSecond = damage, Type = DamageType.Poison, Radius = tornado.Radius, TimeRemaining = tornado.Lifetime, Skill= SkillType.Tornado, TickRate = tornado.TickRate, HitForce = tornado.HitForce});
        commandBuffer.AddComponent(newEntity, new Source{Entity = caster});
        commandBuffer.AddComponent(newEntity, new AttackTarget{TargetType = TargetType.Enemy});
        // A projectile (so projectile bonuses apply) that wanders along the ground and passes through walls
        commandBuffer.AddComponent(newEntity, new Projectile{Direction = direction, Speed = tornado.Speed, DestroyOnHit = false, FollowGround = true, PassThroughWalls = true});
        commandBuffer.AddComponent(newEntity, new WanderingProjectile{DirectionDuration = tornado.DirectionDuration, SwayRadius = 0.3f, SwaySpeed = 6.6f});
        commandBuffer.AddComponent(newEntity, new InitializeFromSource{});
    }
}
