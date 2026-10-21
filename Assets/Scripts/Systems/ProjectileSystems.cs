using Unity.Burst;
using Unity.Entities;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;

//Run this before projectile system because the direction needs to be modified to follow the target
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
            CollidesWith = (uint)CollisionLayerHelper.Enemy,
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
        foreach (var (projectile, homing, localTransform) in SystemAPI.Query<RefRW<Projectile>, RefRW<HomingProjectile>, RefRW<LocalTransform>>())
        {
            var target = homing.ValueRO.Target;
            if(target != Entity.Null && SystemAPI.HasComponent<LocalTransform>(target))
            {
                var targetDirection = SystemAPI.GetComponentRO<LocalTransform>(target).ValueRO.Position - localTransform.ValueRO.Position;
                var newDirection = Vector3.RotateTowards(projectile.ValueRO.Direction, targetDirection, homing.ValueRO.TurnSpeed * SystemAPI.Time.DeltaTime, 0.0f);
                projectile.ValueRW.Direction = newDirection;
                homing.ValueRW.TurnSpeed += homing.ValueRO.TurnAcceleration * SystemAPI.Time.DeltaTime;
            }
        }
    }
}

[BurstCompile]
partial struct ProjectileSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Projectile movement
        foreach (var (localTransform, projectile) in SystemAPI.Query<RefRW<LocalTransform>, RefRO<Projectile>>())
        {
            localTransform.ValueRW.Position += projectile.ValueRO.Direction * projectile.ValueRO.Speed * SystemAPI.Time.DeltaTime;
        }

    }
}

[UpdateAfter(typeof(CalculateDamageSystem))]
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
