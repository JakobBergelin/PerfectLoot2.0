using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Cursed Hammer: once the caster faces the cursor, has recovered and can pay, it throws a hammer that way. Repeats while
/// held, AttackSpeed apart (the shared AttackRecovery). Also casts it for effects that ask (Echoing Blade).
/// </summary>
[UpdateInGroup(typeof(SkillSystemGroup))]
[BurstCompile]
public partial struct CursedHammerSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<CursedHammer>();
        state.RequireForUpdate<MovementConfig>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // SkillState: no cooldown to report (the time between casts is the shared AttackRecovery); IsActive/resource set by SkillActivationSystem
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var movementConfig = SystemAPI.GetSingleton<MovementConfig>();
        float deltaTime = SystemAPI.Time.DeltaTime;

        // Casts requested by effects: no attack recovery, the normal cost (skipped if it can't be paid)
        foreach (var (hammer, requests, skills, resourceChanges, resource, stats, transform, entity) in
                 SystemAPI.Query<RefRO<CursedHammer>, DynamicBuffer<SkillCastRequest>, DynamicBuffer<Skill>, DynamicBuffer<ResourceChanger>, RefRO<Resource>, RefRO<CalculatedStats>, RefRO<LocalTransform>>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            float available = resource.ValueRO.CurrentValue;
            float cost = SkillUtil.Cost(hammer.ValueRO.ResourceCost, stats.ValueRO.Value);
            while (SkillUtil.TryTakeRequest(requests, SkillType.CursedHammer, out var request))
            {
                if (!request.Free)
                {
                    if (!SkillUtil.CanAfford(available, cost, stats.ValueRO)) continue;
                    available = math.max(0f, available - cost);
                    resourceChanges.Add(new ResourceChanger { Value = -cost });
                }
                Spawn(commandBuffer, entity, hammer.ValueRO, transform.ValueRO.Position, request.Direction);
                SkillUtil.MarkUsed(skills, SkillType.CursedHammer, request.AutoCast);
            }
        }

        var resourceLookup = SystemAPI.GetBufferLookup<ResourceChanger>();
        var skillLookup = SystemAPI.GetBufferLookup<Skill>();
        foreach (var (hammer, transform, desiredDirection, resource, stats, recovery, entity) in
                 SystemAPI.Query<RefRO<CursedHammer>, RefRW<LocalTransform>, RefRO<DesiredDirection>, RefRO<Resource>, RefRO<CalculatedStats>, RefRW<AttackRecovery>>().WithEntityAccess())
        {
            bool facing = SkillUtil.TurnToward(ref transform.ValueRW, desiredDirection.ValueRO, movementConfig, deltaTime);
            if (recovery.ValueRO.Remaining > 0f) continue;

            float cost = SkillUtil.Cost(hammer.ValueRO.ResourceCost, stats.ValueRO.Value);
            if (!SkillUtil.CanAfford(resource.ValueRO.CurrentValue, cost, stats.ValueRO) || !facing) continue;

            Spawn(commandBuffer, entity, hammer.ValueRO, transform.ValueRO.Position, desiredDirection.ValueRO.Direction);
            recovery.ValueRW.Remaining += hammer.ValueRO.AttackSpeed;
            if (resourceLookup.HasBuffer(entity)) resourceLookup[entity].Add(new ResourceChanger { Value = -cost });
            if (skillLookup.HasBuffer(entity)) SkillUtil.MarkUsed(skillLookup[entity], SkillType.CursedHammer);
        }
    }

    /// <summary>One hammer from the caster toward direction. The caller pays the cost.</summary>
    public static void Spawn(EntityCommandBuffer commandBuffer, Entity caster, in CursedHammer hammer, float3 casterPosition, float3 direction)
    {
        var entity = commandBuffer.CreateEntity();
        commandBuffer.AddComponent(entity, new LocalTransform { Position = casterPosition + direction, Rotation = quaternion.LookRotationSafe(direction, math.up()), Scale = 1f });
        commandBuffer.AddComponent(entity, new Projectile { Direction = direction, Speed = hammer.StartSpeed, DestroyOnHit = true, FollowGround = true });
        commandBuffer.AddComponent(entity, new CursedHammerProjectile
        {
            Acceleration = hammer.Acceleration, MaxSpeed = hammer.MaxSpeed, HitRadius = hammer.HitRadius, SpinPerMeter = hammer.SpinPerMeter,
            Damage = hammer.Damage, Radius = hammer.Radius,
        });
        commandBuffer.AddComponent(entity, new Source { Entity = caster });
        commandBuffer.AddComponent(entity, new InitializeFromSource());   // Pierce, bounces and projectile speed
    }
}

/// <summary>
/// Flying hammers, before ProjectileSystem moves them: each speeds up by Acceleration (up to MaxSpeed), tumbles end over
/// end, and looks along this frame's move. The first enemy it would touch (HitRadius) makes it burst there; with pierce
/// left it flies on, otherwise it's gone. A wall it would hit makes it burst there; ProjectileSystem then bounces it off
/// or removes it. Missed hammers go like other projectiles (ProjectileCleanupSystem).
/// </summary>
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateBefore(typeof(ProjectileSystem))]
[BurstCompile]
public partial struct CursedHammerFlightSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<CursedHammerProjectile>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        // Bursts start next frame, so their one-frame attack isn't removed before it hits; hammers go this frame, like other projectiles
        var burstBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var destroyBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var enemies = SystemAPI.GetComponentLookup<Enemy>(true);
        float deltaTime = SystemAPI.Time.DeltaTime;
        var enemyFilter = new CollisionFilter { BelongsTo = ~0u, CollidesWith = (uint)CollisionLayerHelper.AnyEnemy, GroupIndex = 0 };
        var hits = new NativeList<ColliderCastHit>(Allocator.Temp);

        foreach (var (hammer, projectile, transform, source, entity) in
                 SystemAPI.Query<RefRW<CursedHammerProjectile>, RefRW<Projectile>, RefRW<LocalTransform>, RefRO<Source>>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            var h = hammer.ValueRO;
            if (projectile.ValueRO.Speed < h.MaxSpeed) projectile.ValueRW.Speed = math.min(projectile.ValueRO.Speed + h.Acceleration * deltaTime, h.MaxSpeed);

            // This frame's move, as ProjectileSystem will make it
            var p = projectile.ValueRO;
            var direction = p.FollowGround ? math.normalizesafe(new float3(p.Direction.x, 0f, p.Direction.z), p.Direction) : p.Direction;
            float distance = p.CurrentSpeed * deltaTime;
            var start = transform.ValueRO.Position;

            hammer.ValueRW.SpinAngle += distance * h.SpinPerMeter;
            transform.ValueRW.Rotation = math.mul(quaternion.LookRotationSafe(direction, math.up()), quaternion.RotateX(hammer.ValueRO.SpinAngle));

            // The nearest enemy on the way
            hits.Clear();
            physicsWorld.SphereCastAll(start, h.HitRadius, direction, distance, ref hits, enemyFilter);
            var target = Entity.Null;
            float fraction = float.MaxValue;
            foreach (var hit in hits)
            {
                if (hit.Entity == h.LastHit || hit.Entity == source.ValueRO.Entity || !enemies.HasComponent(hit.Entity) || hit.Fraction >= fraction) continue;
                target = hit.Entity;
                fraction = hit.Fraction;
            }
            if (target != Entity.Null)
            {
                Burst(burstBuffer, h, start + direction * distance * fraction, source.ValueRO);
                hammer.ValueRW.LastHit = target;
                if (p.PierceCount > 0) projectile.ValueRW.PierceCount--;
                else
                {
                    destroyBuffer.AddComponent<DestroyWithChildren>(entity);
                    continue;
                }
            }

            // A wall on the way
            var wallFilter = p.FollowGround ? ProjectileUtil.WallFilter : ProjectileUtil.WorldFilter;
            if (!p.PassThroughWalls && physicsWorld.CastRay(new RaycastInput { Start = start, End = start + direction * distance, Filter = wallFilter }, out var wall))
            {
                Burst(burstBuffer, h, wall.Position, source.ValueRO);
                hammer.ValueRW.LastHit = Entity.Null;
            }
        }
    }

    // A one-frame lightning attack where the hammer hit; the source's damage %, conversion and area of effect apply (InitializeFromSource)
    static void Burst(EntityCommandBuffer commandBuffer, in CursedHammerProjectile hammer, float3 position, in Source source)
    {
        var burst = commandBuffer.CreateEntity();
        commandBuffer.AddComponent(burst, LocalTransform.FromPosition(position));
        commandBuffer.AddComponent(burst, new Attack { Origin = DamageLabel.CursedHammer, Damage = hammer.Damage, Type = DamageType.Lightning, Radius = hammer.Radius, DamageHierarchy = DamageHierarchy.Main });
        commandBuffer.AddComponent(burst, new AttackTarget { TargetType = TargetType.Enemy });
        commandBuffer.AddComponent(burst, source);
        commandBuffer.AddComponent(burst, new InitializeFromSource());
        commandBuffer.AddComponent(burst, new DestroyWithChildren());
    }
}

/// <summary>Placeholder art: each flying hammer as lines, a handle and a head, tumbling with it.</summary>
[UpdateInGroup(typeof(FeedbackSystemGroup))]
[BurstCompile]
partial struct CursedHammerUISystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var transform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<CursedHammerProjectile>())
        {
            var t = transform.ValueRO;
            var up = math.mul(t.Rotation, math.up());
            var forward = math.mul(t.Rotation, new float3(0f, 0f, 1f));
            var bottom = t.Position - up * 0.45f;
            var top = t.Position + up * 0.45f;
            Debug.DrawLine(bottom, top, Color.mediumPurple);
            Debug.DrawLine(top - forward * 0.3f, top + forward * 0.3f, Color.cyan);
            Debug.DrawLine(top - forward * 0.3f - up * 0.15f, top + forward * 0.3f - up * 0.15f, Color.cyan);
        }
    }
}
