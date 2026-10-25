using Unity.Burst;
using Unity.Entities;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;

//Run this before projectile system because the direction needs to be modified to follow the target
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateBefore(typeof(ProjectileSystem))]
// TODO run after all updates of stats
[BurstCompile]
partial struct HomingProjectileSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<CombatConfig>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var combatConfig = SystemAPI.GetSingleton<CombatConfig>();

        // Find target if missing, looking at HomingType
        var physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        NativeList<DistanceHit> outHits = new NativeList<DistanceHit>(Allocator.Temp);
        var collisionFilter = new CollisionFilter
        {
            BelongsTo = ~0u,
            CollidesWith = (uint)CollisionLayerHelper.AnyEnemy,
            GroupIndex = 0,
        };

        foreach (var (homing, localTransform, entity) in SystemAPI.Query<RefRW<HomingProjectile>, RefRW<LocalTransform>>().WithEntityAccess())
        {
            var target = homing.ValueRO.Target;
            if(target == Entity.Null)
            {
                //TODO player range
                physicsWorldSingleton.OverlapSphere(localTransform.ValueRO.Position, combatConfig.HomingTargetRange, ref outHits, collisionFilter);
                if(!outHits.IsEmpty)
                {
                    Entity bestEntity = Entity.Null;
                    float bestValue = homing.ValueRO.HomingType == HomingType.Strongest ? 0.0f : float.MaxValue;
                    foreach(var hit in outHits)
                    {
                        if(hit.Entity != entity)
                        {
                            if (SystemAPI.HasComponent<Source>(entity) && SystemAPI.GetComponentRO<Source>(entity).ValueRO.Entity == hit.Entity)
                            {
                                continue;
                            }
                            if (SystemAPI.HasComponent<AttackTarget>(entity))
                            {
                                var targetType = SystemAPI.GetComponentRO<AttackTarget>(entity).ValueRO.TargetType;
                                if(targetType == TargetType.Player && !SystemAPI.HasComponent<Player>(hit.Entity))
                                {
                                    continue;
                                }
                                else if(targetType == TargetType.Enemy && !SystemAPI.HasComponent<Enemy>(hit.Entity))
                                {
                                    continue;
                                }
                            }

                            if(SystemAPI.HasComponent<Health>(hit.Entity))
                            {
                                switch(homing.ValueRO.HomingType)
                                {
                                    case HomingType.Closest:
                                        {
                                            var targetPosition = SystemAPI.GetComponentRO<LocalTransform>(hit.Entity).ValueRO.Position;
                                            var distance = Vector3.Distance(targetPosition, localTransform.ValueRO.Position);
                                            if(distance < bestValue)
                                            {
                                                bestValue = distance;
                                                bestEntity = hit.Entity;
                                            }
                                            break;
                                        }

                                    case HomingType.Strongest:
                                        {
                                            var maxHealth = SystemAPI.GetComponentRO<Health>(hit.Entity).ValueRO.MaxValue;
                                            if(maxHealth > bestValue)
                                            {
                                                bestValue = maxHealth;
                                                bestEntity = hit.Entity;
                                            }
                                            break;
                                        }
                                        
                                    
                                    case HomingType.Weakest:
                                        {
                                            var maxHealth = SystemAPI.GetComponentRO<Health>(hit.Entity).ValueRO.MaxValue;
                                            if(maxHealth < bestValue)
                                            {
                                                bestValue = maxHealth;
                                                bestEntity = hit.Entity;
                                            }
                                            break;
                                        }
                                }
                            }  
                        }
                    }
                    if(bestEntity != Entity.Null)
                    {
                        homing.ValueRW.Target = bestEntity;
                    }
                }
            }
            else if(!SystemAPI.Exists(target))
            {
                // TODO, destroy timer?
                homing.ValueRW.Target = Entity.Null;
            }
        }

        // Change direction to target
        var worldFilter = ProjectileUtil.WorldFilter;
        foreach (var (projectile, homing, localTransform) in SystemAPI.Query<RefRW<Projectile>, RefRW<HomingProjectile>, RefRW<LocalTransform>>())
        {
            var target = homing.ValueRO.Target;
            if(target != Entity.Null && SystemAPI.HasComponent<LocalTransform>(target))
            {
                var position = localTransform.ValueRO.Position;
                var targetDirection = SystemAPI.GetComponentRO<LocalTransform>(target).ValueRO.Position - position;
                float targetDistance = math.length(targetDirection);

                // Weak avoidance: with the ground or a wall ahead and closer than the target, bend the goal a little
                // along the surface normal (stronger the closer it is). Never when the target is reached first.
                var forward = math.normalizesafe(projectile.ValueRO.Direction);
                var lookahead = new RaycastInput { Start = position, End = position + forward * combatConfig.HomingAvoidLookahead, Filter = worldFilter };
                if (targetDistance > 1e-4f && physicsWorldSingleton.CastRay(lookahead, out var obstacle))
                {
                    float obstacleDistance = obstacle.Fraction * combatConfig.HomingAvoidLookahead;
                    if (obstacleDistance < targetDistance)
                    {
                        float closeness = 1.0f - obstacle.Fraction;
                        targetDirection = targetDirection / targetDistance + obstacle.SurfaceNormal * combatConfig.HomingAvoidStrength * closeness;
                    }
                }

                var newDirection = Vector3.RotateTowards(projectile.ValueRO.Direction, targetDirection, homing.ValueRO.TurnSpeed * SystemAPI.Time.DeltaTime, 0.0f);
                projectile.ValueRW.Direction = newDirection;
                homing.ValueRW.TurnSpeed += homing.ValueRO.TurnAcceleration * SystemAPI.Time.DeltaTime;
            }
        }
    }
}

/// <summary>
/// Picks WanderingProjectile directions and applies their sway, before ProjectileSystem moves them.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateBefore(typeof(ProjectileSystem))]
partial struct WanderingProjectileSystem : ISystem
{
    uint _frame;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var random = Unity.Mathematics.Random.CreateFromIndex(_frame++);
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (projectile, wandering, localTransform) in SystemAPI.Query<RefRW<Projectile>, RefRW<WanderingProjectile>, RefRW<LocalTransform>>())
        {
            wandering.ValueRW.Elapsed += deltaTime;
            if (wandering.ValueRO.Elapsed >= wandering.ValueRO.DirectionDuration)
            {
                wandering.ValueRW.Elapsed = 0f;
                var flat = new float3(random.NextFloat(-1f, 1f), 0f, random.NextFloat(-1f, 1f));
                projectile.ValueRW.Direction = math.normalizesafe(flat, projectile.ValueRO.Direction);
            }

            // Sway: swap last frame's circle offset for this frame's, so the path's center is what moves
            wandering.ValueRW.SwayAngle += wandering.ValueRO.SwaySpeed * deltaTime;
            math.sincos(wandering.ValueRO.SwayAngle, out float sin, out float cos);
            var offset = new float3(cos, 0f, sin) * wandering.ValueRO.SwayRadius;
            localTransform.ValueRW.Position += offset - wandering.ValueRO.SwayOffset;
            wandering.ValueRW.SwayOffset = offset;
        }
    }
}

/// <summary>
/// Moves projectiles. Free-flying ones hit the ground and walls; FollowGround ones move flat at their spawn height
/// above the ground (following slopes) and only hit walls; PassThroughWalls ignores walls. A hit stops the projectile
/// at the hit point and bounces it off (if it has bounces left) or destroys it at the end of the frame. Hit detection
/// still runs at the hit point first, so an enemy right next to the wall is still hit.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(MovementSystemGroup))]
partial struct ProjectileSystem : ISystem
{
    // A ground probe starts this far above the projectile, so rising slopes are found too
    const float GroundProbeUp = 2f;
    const float GroundProbeDown = 20f;

    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        var commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var worldFilter = ProjectileUtil.WorldFilter;
        var wallFilter = ProjectileUtil.WallFilter;
        var groundFilter = ProjectileUtil.GroundFilter;
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (localTransform, projectile, entity) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<Projectile>>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            var start = localTransform.ValueRO.Position;
            var direction = projectile.ValueRO.Direction;
            bool followGround = projectile.ValueRO.FollowGround;

            if (followGround)
            {
                // Its height above the ground, measured once
                if (!projectile.ValueRO.GroundHeightSet)
                {
                    projectile.ValueRW.GroundHeight = TryFindGround(physicsWorldSingleton, start, groundFilter, out float groundY) ? start.y - groundY : -1f;
                    projectile.ValueRW.GroundHeightSet = true;
                }
                // Flat, at the same speed
                var flat = new float3(direction.x, 0f, direction.z);
                direction = math.normalizesafe(flat) * math.length(direction);
            }
            var end = start + direction * projectile.ValueRO.Speed * deltaTime;

            // The center crossing the ground or a wall counts as a hit (the attack sphere reaches nearby enemies first)
            bool checkCollision = !(followGround && projectile.ValueRO.PassThroughWalls);
            var filter = followGround ? wallFilter : projectile.ValueRO.PassThroughWalls ? groundFilter : worldFilter;
            if (checkCollision && physicsWorldSingleton.CastRay(new RaycastInput { Start = start, End = end, Filter = filter }, out var hit))
            {
                if (projectile.ValueRO.BounceCount > 0)
                {
                    projectile.ValueRW.BounceCount--;
                    projectile.ValueRW.Direction = math.reflect(projectile.ValueRO.Direction, hit.SurfaceNormal);
                    // Just off the surface, so the next frame's ray doesn't start inside it
                    localTransform.ValueRW.Position = hit.Position + hit.SurfaceNormal * 0.01f;
                }
                else
                {
                    localTransform.ValueRW.Position = hit.Position;
                    commandBuffer.AddComponent<DestroyWithChildren>(entity);
                }
                continue;
            }

            // Keep the spawn height above the ground; over water or a gap it keeps its height
            if (followGround && projectile.ValueRO.GroundHeight >= 0f && TryFindGround(physicsWorldSingleton, end, groundFilter, out float groundAtEnd))
                end.y = groundAtEnd + projectile.ValueRO.GroundHeight;

            localTransform.ValueRW.Position = end;
        }
    }

    static bool TryFindGround(in PhysicsWorldSingleton physics, float3 position, CollisionFilter groundFilter, out float groundY)
    {
        var input = new RaycastInput
        {
            Start = position + new float3(0f, GroundProbeUp, 0f),
            End = position - new float3(0f, GroundProbeDown, 0f),
            Filter = groundFilter,
        };
        bool found = physics.CastRay(input, out var hit);
        groundY = found ? hit.Position.y : 0f;
        return found;
    }
}

public static class ProjectileUtil
{
    /// <summary>What free-flying projectiles collide with besides units: the ground and walls.</summary>
    public static CollisionFilter WorldFilter => new CollisionFilter
    {
        BelongsTo = ~0u,
        CollidesWith = (uint)(CollisionLayerHelper.Ground | CollisionLayerHelper.Wall),
        GroupIndex = 0,
    };

    /// <summary>Walls only: what ground-following projectiles collide with.</summary>
    public static CollisionFilter WallFilter => new CollisionFilter
    {
        BelongsTo = ~0u,
        CollidesWith = (uint)CollisionLayerHelper.Wall,
        GroupIndex = 0,
    };

    public static CollisionFilter GroundFilter => new CollisionFilter
    {
        BelongsTo = ~0u,
        CollidesWith = (uint)CollisionLayerHelper.Ground,
        GroupIndex = 0,
    };
}

[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateBefore(typeof(DamagingSystem))]
[BurstCompile]
public partial class ProjectileDestroySystem : SystemBase
{
    [BurstCompile]
    protected override void OnUpdate()
    {
        var toDestroy = new NativeList<Entity>(Allocator.Temp);
        foreach (var (projectile, attack, source, entity) in SystemAPI.Query<RefRW<Projectile>, RefRO<Attack>, RefRO<Source>>().WithEntityAccess())
        {
            if(attack.ValueRO.Events.OnHit && projectile.ValueRO.DestroyOnHit)
            {
                if(projectile.ValueRO.PierceCount > 0)
                {
                    projectile.ValueRW.PierceCount--;
                    continue;
                }
                if(projectile.ValueRO.BounceCount > 0)
                {
                    projectile.ValueRW.BounceCount--;
                    projectile.ValueRW.Direction = Vector3.Reflect(projectile.ValueRW.Direction, attack.ValueRO.Events.OnHitNormal);
                    continue;
                }
                if(SystemAPI.GetComponent<CalculatedStats>(source.ValueRO.Entity).PierceOnCrit && attack.ValueRO.Events.IsCrit)
                {
                    Debug.Log("pierce on crit");
                    continue;
                }

                toDestroy.Add(entity);
            }
        }

        if(!toDestroy.IsEmpty)
        {
            EntityManager.AddComponent<DestroyWithChildren>(toDestroy.AsArray());
        }
    }
}
