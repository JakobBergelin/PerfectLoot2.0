using Unity.Burst;
using Unity.Entities;
using Unity.Physics;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using System.Linq;
using System;

[BurstCompile]
[UpdateInGroup(typeof(AttackSystemGroup))]
public partial class AttackSystem : SystemBase
{
    private Unity.Mathematics.Random _random;

    [BurstCompile]
    protected override void OnCreate()
    {
        _random = Unity.Mathematics.Random.CreateFromIndex((uint)Environment.TickCount);
    }

    public static void CalculateCrit(Entity source, EntityManager entityManager, ref Unity.Mathematics.Random random, ref float damage, ref bool isCrit)
    {
        if(source != Entity.Null && entityManager.HasComponent<CalculatedStats>(source))
        {
            var sourceStats = entityManager.GetComponentData<CalculatedStats>(source);
            var randomValue = random.NextFloat();

            if(randomValue < sourceStats.Value.CriticalChance)
            {
                isCrit = true;
                damage *= 1.0f + sourceStats.Value.CriticalDamage;                        
            }
        }
    }

    /// <summary>The direction a hit pushes in: horizontally away from the attack. Random if the target is on its center.</summary>
    public static float3 HitDirection(float3 attackPosition, float3 targetPosition, ref Unity.Mathematics.Random random)
    {
        var away = targetPosition - attackPosition;
        away.y = 0f;
        if (math.lengthsq(away) > 1e-6f) return math.normalize(away);
        var flat = random.NextFloat2Direction();
        return new float3(flat.x, 0f, flat.y);
    }

    [BurstCompile]
    protected override void OnUpdate()
    {
        var physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        NativeList<DistanceHit> outHits = new NativeList<DistanceHit>(Allocator.Temp);
        // TODO only group with hitable entities
        var collisionFilter = new CollisionFilter
        {
            BelongsTo = ~0u,
            CollidesWith = (uint)CollisionLayerHelper.Unit,
            GroupIndex = 0,
        };

        // IgnoreEntity buffers are added up front by AddIgnoreEntityBufferSystem, so every hit this frame is recorded
        foreach (var (attack, localTransform, source, ignoreBuffer, entity) in SystemAPI.Query<RefRW<Attack>, RefRO<LocalTransform>, RefRO<Source>, DynamicBuffer<IgnoreEntity>>().WithEntityAccess())
        {
            outHits.Clear();
            physicsWorldSingleton.OverlapSphere(localTransform.ValueRO.Position, attack.ValueRO.Radius, ref outHits, collisionFilter);
            if(!outHits.IsEmpty)
            {
                foreach(var hit in outHits)
                {
                    if(hit.Entity != entity)
                    {
                        if (source.ValueRO.Entity == hit.Entity)
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

                        bool skipEntity = false;
                        foreach(var ignored in ignoreBuffer)
                        {
                            if(ignored.Entity == hit.Entity)
                            {
                                skipEntity = true;
                                break;
                            }
                        }
                        if(skipEntity)
                        {
                            continue;
                        }

                        if(SystemAPI.HasComponent<Health>(hit.Entity) && SystemAPI.HasBuffer<DamageInstance>(hit.Entity))
                        {
                            bool isCrit = false;
                            var damage = attack.ValueRO.Damage;

                            CalculateCrit(source.ValueRO.Entity, EntityManager, ref _random, ref damage, ref isCrit);

                            attack.ValueRW.Events.OnHit = true;
                            attack.ValueRW.Events.OnHitNormal = hit.SurfaceNormal;
                            attack.ValueRW.Events.IsCrit = isCrit;

                            // Written directly (not via ECB) so CalculateDamageSystem handles it this frame
                            var hitDirection = HitDirection(localTransform.ValueRO.Position, SystemAPI.GetComponentRO<LocalTransform>(hit.Entity).ValueRO.Position, ref _random);
                            SystemAPI.GetBuffer<DamageInstance>(hit.Entity).Add(new DamageInstance{Damage = damage, Type = attack.ValueRO.Type, Hierarchy = attack.ValueRO.DamageHierarchy, Source = source.ValueRO.Entity, IsCrit = isCrit, HitDirection = hitDirection, HitForce = attack.ValueRO.HitForce});
                            //TODO dynamic graceperiod depending on skill and attackspeed
                            ignoreBuffer.Add(new IgnoreEntity{Entity = hit.Entity, GraceTimer = 0.3f});
                        }
                    }
                }
            }
        }
    }
}


// Gives every attack an IgnoreEntity buffer before AttackSystem runs, whichever system created it
[UpdateInGroup(typeof(AttackSystemGroup), OrderFirst = true)]
public partial struct AddIgnoreEntityBufferSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var query = SystemAPI.QueryBuilder().WithAll<Attack>().WithNone<IgnoreEntity>().Build();
        if(!query.IsEmpty)
        {
            state.EntityManager.AddComponent<IgnoreEntity>(query);
        }
    }
}

[UpdateInGroup(typeof(AttackSystemGroup))]
public partial struct IgnoreEntitySystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var ignoreBuffer in SystemAPI.Query<DynamicBuffer<IgnoreEntity>>())
        {
            for(int i = 0; i < ignoreBuffer.Length; i++)
            {
                ref var instance = ref ignoreBuffer.ElementAt(i);
                instance.GraceTimer -= SystemAPI.Time.DeltaTime;
                if(instance.GraceTimer <= 0)
                {
                    ignoreBuffer.RemoveAt(i);
                    i--;
                }
            }
        }
    }
}

[UpdateBefore(typeof(AttackSystemGroup))]
public partial struct ResetAttackTriggersSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var attack in SystemAPI.Query<RefRW<Attack>>())
        {
            attack.ValueRW.Events.OnHit = false;
            attack.ValueRW.Events.IsCrit = false;
        }
    }
}

[BurstCompile]
[UpdateInGroup(typeof(AttackSystemGroup))]
public partial class PeriodicAttackSystem : SystemBase
{
    private Unity.Mathematics.Random _random;
    private EndSimulationEntityCommandBufferSystem _endSimulationEntityCommandBufferSystem;
    [BurstCompile]
    protected override void OnCreate()
    {
        _random = Unity.Mathematics.Random.CreateFromIndex((uint)Environment.TickCount);
        _endSimulationEntityCommandBufferSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<EndSimulationEntityCommandBufferSystem>();
    }
    [BurstCompile]
    protected override void OnUpdate()
    {
        // Projectile movement and hit detection
        var physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        NativeList<DistanceHit> outHits = new NativeList<DistanceHit>(Allocator.Temp);
        // TODO only group with hitable entities
        var collisionFilter = new CollisionFilter
        {
            BelongsTo = ~0u,
            CollidesWith = (uint)CollisionLayerHelper.AnyEnemy,
            GroupIndex = 0,
        };


        foreach (var (localTransform, periodicAttack, source, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<PeriodicAttack>, RefRO<Source>>().WithEntityAccess())
        {
            outHits.Clear();
            physicsWorldSingleton.OverlapSphere(localTransform.ValueRO.Position, periodicAttack.ValueRO.Radius, ref outHits, collisionFilter);
            if(!outHits.IsEmpty)
            {
                foreach(var hit in outHits)
                {
                    if(hit.Entity != entity)
                    {
                        if (source.ValueRO.Entity == hit.Entity)
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
                            DynamicBuffer<PeriodicDamageInstance> periodicDamageBuffer = SystemAPI.GetBuffer<PeriodicDamageInstance>(hit.Entity);;
                            int index = -1;
                            for(int i = 0; i < periodicDamageBuffer.Length; i++)
                            {
                                if(periodicDamageBuffer[i].Skill == periodicAttack.ValueRO.Skill)
                                {
                                    index = i;
                                    break;
                                }
                            }
                            float damage = periodicAttack.ValueRO.DamagePerSecond * SystemAPI.Time.DeltaTime;
                            // Away from where the attack is now; the tick uses the latest one
                            var hitDirection = AttackSystem.HitDirection(localTransform.ValueRO.Position, SystemAPI.GetComponentRO<LocalTransform>(hit.Entity).ValueRO.Position, ref _random);
                            if(index == -1)
                            {
                                periodicDamageBuffer.Add(new PeriodicDamageInstance{AccumilatedDamage = damage, Type = periodicAttack.ValueRO.Type, Hierarchy = DamageHierarchy.Main, Skill = periodicAttack.ValueRO.Skill, Tick = periodicAttack.ValueRO.TickRate, Source = source.ValueRO.Entity,
                                    HitDirection = hitDirection, HitForce = periodicAttack.ValueRO.HitForce});
                            }
                            else
                            {
                                ref var instance = ref periodicDamageBuffer.ElementAt(index);
                                instance.AccumilatedDamage += damage;
                                instance.HitDirection = hitDirection;
                            }
                        }
                        
                    }
                }
            }
        }

        NativeList<Entity> toDestroy = new NativeList<Entity>(Allocator.Temp);
        foreach (var (periodicAttack, entity) in SystemAPI.Query<RefRW<PeriodicAttack>>().WithEntityAccess())
        {
            periodicAttack.ValueRW.TimeRemaining -= SystemAPI.Time.DeltaTime;
            if(periodicAttack.ValueRO.TimeRemaining <= 0.0f)
            {
                toDestroy.Add(entity);
            }
        }

        if(!toDestroy.IsEmpty)
        {
            var commandBuffer = _endSimulationEntityCommandBufferSystem.CreateCommandBuffer();
            commandBuffer.AddComponent<DestroyWithChildren>(toDestroy.AsArray());
        }
    }
}

[BurstCompile]
[UpdateInGroup(typeof(AttackSystemGroup))]
[UpdateAfter(typeof(PeriodicAttackSystem))]
public partial class AccumilatedPeriodicAttackSystem : SystemBase
{
    private Unity.Mathematics.Random _random;

    protected override void OnCreate()
    {
        _random = Unity.Mathematics.Random.CreateFromIndex((uint)Environment.TickCount);
    }

    [BurstCompile]
    protected override void OnUpdate()
    {
        foreach (var (periodicDamageInstance, DamageInstance, entity) in SystemAPI.Query<DynamicBuffer<PeriodicDamageInstance>, DynamicBuffer<DamageInstance>>().WithEntityAccess())
        {
            for(int i = 0; i < periodicDamageInstance.Length; i++)
            {
                ref var instance = ref periodicDamageInstance.ElementAt(i);
                instance.Tick -= SystemAPI.Time.DeltaTime;

                if(instance.Tick <= 0.0f)
                {
                    bool isCrit = false;
                    var damage = instance.AccumilatedDamage;

                    AttackSystem.CalculateCrit(instance.Source, EntityManager, ref _random, ref damage, ref isCrit);

                    DamageInstance.Add(new DamageInstance{Damage = damage, Type = instance.Type, Hierarchy = instance.Hierarchy, Source = instance.Source, IsCrit = isCrit, HitDirection = instance.HitDirection, HitForce = instance.HitForce});
                    periodicDamageInstance.RemoveAt(i);
                    i--;
                }
            }
        }
    }
}
