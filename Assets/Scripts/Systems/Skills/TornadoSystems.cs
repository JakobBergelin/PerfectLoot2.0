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
        // SkillState: no cooldown to report (the time between attacks is the shared AttackRecovery); IsActive/resource set by SkillActivationSystem
        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
        var movementConfig = SystemAPI.GetSingleton<MovementConfig>();

        // Casts requested by effects (e.g. Echoing Blade): no attack recovery, the normal cost (skipped if it can't be paid)
        foreach (var (tornado, requests, skills, resourceChanges, resource, localTransform, stats, entity) in
                 SystemAPI.Query<RefRO<Tornado>, DynamicBuffer<SkillCastRequest>, DynamicBuffer<Skill>, DynamicBuffer<ResourceChanger>, RefRO<Resource>, RefRO<LocalTransform>, RefRO<CalculatedStats>>()
                          .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            float available = resource.ValueRO.CurrentValue;
            float cost = SkillUtil.Cost(tornado.ValueRO.ResourceCost, stats.ValueRO.Value);
            while (SkillUtil.TryTakeRequest(requests, SkillType.Tornado, out var request))
            {
                if (!SkillUtil.CanAfford(available, cost, stats.ValueRO)) continue;
                available = Mathf.Max(0f, available - cost);
                Spawn(commandBuffer, entity, tornado.ValueRO, stats.ValueRO.Value, localTransform.ValueRO.Position, request.Direction, ref _random);
                resourceChanges.Add(new ResourceChanger{Value = -cost});
                SkillUtil.MarkUsed(skills, SkillType.Tornado, request.AutoCast);
            }
        }

        foreach (var (tornado, localTransform, desiredDirection, resource, stats, recovery, entity) in SystemAPI.Query<RefRO<Tornado>, RefRW<LocalTransform>, RefRO<DesiredDirection>, RefRO<Resource>, RefRO<CalculatedStats>, RefRW<AttackRecovery>>().WithEntityAccess())
        {
            bool facing = SkillUtil.TurnToward(ref localTransform.ValueRW, desiredDirection.ValueRO, movementConfig, SystemAPI.Time.DeltaTime);
            if(recovery.ValueRO.Remaining > 0f)
            {
                continue;
            }

            float cost = SkillUtil.Cost(tornado.ValueRO.ResourceCost, stats.ValueRO.Value);
            if(!SkillUtil.CanAfford(resource.ValueRO.CurrentValue, cost, stats.ValueRO))
            {
                continue;
            }

            if(!facing)
            {
                continue;
            }

            // Fires as soon as it faces the cursor; the wait comes after (shared with the other repeated attacks)
            Spawn(commandBuffer, entity, tornado.ValueRO, stats.ValueRO.Value, localTransform.ValueRO.Position, desiredDirection.ValueRO.Direction, ref _random);
            recovery.ValueRW.Remaining += tornado.ValueRO.AttackSpeed;

            SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger{Value = -cost});
            if(SystemAPI.HasBuffer<Skill>(entity)) SkillUtil.MarkUsed(SystemAPI.GetBuffer<Skill>(entity), SkillType.Tornado);
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
