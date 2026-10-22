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
/// Moves projectiles. A projectile whose path this frame crosses the ground or a wall stops at the hit point and
/// bounces off it (if it has bounces left) or is destroyed at the end of the frame. Hit detection still runs at the
/// hit point first, so an enemy right next to the wall is still hit.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(MovementSystemGroup))]
partial struct ProjectileSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        var commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var worldFilter = ProjectileUtil.WorldFilter;
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (localTransform, projectile, entity) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<Projectile>>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            var start = localTransform.ValueRO.Position;
            var end = start + projectile.ValueRO.Direction * projectile.ValueRO.Speed * deltaTime;

            // The center crossing the ground or a wall counts as a hit (the attack sphere reaches nearby enemies first)
            if (physicsWorldSingleton.CastRay(new RaycastInput { Start = start, End = end, Filter = worldFilter }, out var hit))
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

            localTransform.ValueRW.Position = end;
        }
    }
}

public static class ProjectileUtil
{
    /// <summary>What projectiles collide with besides units: the ground and walls.</summary>
    public static CollisionFilter WorldFilter => new CollisionFilter
    {
        BelongsTo = ~0u,
        CollidesWith = (uint)(CollisionLayerHelper.Ground | CollisionLayerHelper.Wall),
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
