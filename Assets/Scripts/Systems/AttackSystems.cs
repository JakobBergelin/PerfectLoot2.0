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

    public static void CalculateCrit(Entity source, Entity target, EntityManager entityManager, ref Unity.Mathematics.Random random, ref float damage, ref bool isCrit, float chanceBonus = 0f)
    {
        if(source != Entity.Null && entityManager.HasComponent<CalculatedStats>(source))
        {
            var sourceStats = entityManager.GetComponentData<CalculatedStats>(source);
            var randomValue = random.NextFloat();

            if(randomValue < sourceStats.Value.CriticalChance + chanceBonus || IsAmbush(source, target, entityManager))
            {
                isCrit = true;
                damage *= 1.0f + sourceStats.Value.CriticalDamage;
            }
        }
    }

    // Ambush legendary power: the target is still at (or above) the power's share of its max health
    static bool IsAmbush(Entity source, Entity target, EntityManager entityManager)
    {
        if(!entityManager.HasComponent<Ambush>(source) || !entityManager.HasComponent<Health>(target)) return false;
        var health = entityManager.GetComponentData<Health>(target);
        return health.MaxValue > 0.0f && health.CurrentValue >= health.MaxValue * entityManager.GetComponentData<Ambush>(source).HealthThreshold;
    }

    /// <summary>
    /// The direction a hit pushes in: horizontally away from the attack. Random if the target is on its center. For
    /// attacks with an AttackShape, AttackShapes.HitDirection pushes away from the shape's centre line instead.
    /// </summary>
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
            // A sphere of Radius, or its AttackShape
            bool hasShape = SystemAPI.HasComponent<AttackShape>(entity);
            var shape = hasShape ? SystemAPI.GetComponent<AttackShape>(entity) : default;
            AttackShapes.Overlap(ref physicsWorldSingleton, localTransform.ValueRO, attack.ValueRO.Radius, hasShape, shape, ref outHits, collisionFilter);
            // The hit target closest to the attack's center, marked IsCenterTarget once all hits are in
            var centerTarget = Entity.Null;
            int centerIndex = -1;
            float centerDistanceSq = float.MaxValue;
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
                            if(targetType == TargetType.Player && !SystemAPI.HasComponent<EnemyTargetable>(hit.Entity))
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

                            // Sub damage is sized from the hit that caused it, so it doesn't crit again
                            if(attack.ValueRO.DamageHierarchy == DamageHierarchy.Main)
                                CalculateCrit(source.ValueRO.Entity, hit.Entity, EntityManager, ref _random, ref damage, ref isCrit, attack.ValueRO.CriticalChanceBonus);

                            attack.ValueRW.Events.OnHit = true;
                            attack.ValueRW.Events.OnHitNormal = hit.SurfaceNormal;
                            attack.ValueRW.Events.IsCrit = isCrit;

                            // Written directly (not via ECB) so CalculateDamageSystem handles it this frame
                            var targetPosition = SystemAPI.GetComponentRO<LocalTransform>(hit.Entity).ValueRO.Position;
                            var hitDirection = AttackShapes.HitDirection(localTransform.ValueRO, hasShape, targetPosition, ref _random);
                            var damageBuffer = SystemAPI.GetBuffer<DamageInstance>(hit.Entity);
                            damageBuffer.Add(new DamageInstance{Origin = attack.ValueRO.Origin, Damage = damage, Type = attack.ValueRO.Type, Hierarchy = attack.ValueRO.DamageHierarchy, Source = source.ValueRO.Entity, IsCrit = isCrit, HitDirection = hitDirection, HitForce = attack.ValueRO.HitForce, Attack = entity});

                            float gracePeriod = attack.ValueRO.GracePeriod > 0f ? attack.ValueRO.GracePeriod : Attack.DefaultGracePeriod;
                            ignoreBuffer.Add(new IgnoreEntity{Entity = hit.Entity, GraceTimer = gracePeriod});

                            float distanceSq = math.distancesq(localTransform.ValueRO.Position, targetPosition);
                            if(distanceSq < centerDistanceSq)
                            {
                                centerDistanceSq = distanceSq;
                                centerTarget = hit.Entity;
                                centerIndex = damageBuffer.Length - 1;
                            }
                        }
                    }
                }
            }
            if(centerTarget != Entity.Null)
            {
                SystemAPI.GetBuffer<DamageInstance>(centerTarget).ElementAt(centerIndex).IsCenterTarget = true;
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
            // A sphere of Radius, or its AttackShape
            bool hasShape = SystemAPI.HasComponent<AttackShape>(entity);
            var shape = hasShape ? SystemAPI.GetComponent<AttackShape>(entity) : default;
            AttackShapes.Overlap(ref physicsWorldSingleton, localTransform.ValueRO, periodicAttack.ValueRO.Radius, hasShape, shape, ref outHits, collisionFilter);
            // The target closest to the attack's center this frame; its accumulating damage is marked IsCenterTarget
            var centerTarget = Entity.Null;
            float centerDistanceSq = float.MaxValue;
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
                            var targetPosition = SystemAPI.GetComponentRO<LocalTransform>(hit.Entity).ValueRO.Position;
                            var hitDirection = AttackShapes.HitDirection(localTransform.ValueRO, hasShape, targetPosition, ref _random);
                            float distanceSq = math.distancesq(localTransform.ValueRO.Position, targetPosition);
                            if(distanceSq < centerDistanceSq)
                            {
                                centerDistanceSq = distanceSq;
                                centerTarget = hit.Entity;
                            }
                            if(index == -1)
                            {
                                periodicDamageBuffer.Add(new PeriodicDamageInstance{Origin = periodicAttack.ValueRO.Origin, AccumilatedDamage = damage, Type = periodicAttack.ValueRO.Type, Hierarchy = DamageHierarchy.Main, Skill = periodicAttack.ValueRO.Skill, Tick = periodicAttack.ValueRO.TickRate, Source = source.ValueRO.Entity,
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
            // Stays marked until the instance ticks
            if(centerTarget != Entity.Null)
            {
                var centerBuffer = SystemAPI.GetBuffer<PeriodicDamageInstance>(centerTarget);
                for(int i = 0; i < centerBuffer.Length; i++)
                {
                    if(centerBuffer[i].Skill == periodicAttack.ValueRO.Skill)
                    {
                        centerBuffer.ElementAt(i).IsCenterTarget = true;
                        break;
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

                    AttackSystem.CalculateCrit(instance.Source, entity, EntityManager, ref _random, ref damage, ref isCrit);

                    DamageInstance.Add(new DamageInstance{Origin = instance.Origin, Damage = damage, Type = instance.Type, Hierarchy = instance.Hierarchy, Source = instance.Source, IsCrit = isCrit, HitDirection = instance.HitDirection, HitForce = instance.HitForce, IsCenterTarget = instance.IsCenterTarget});
                    periodicDamageInstance.RemoveAt(i);
                    i--;
                }
            }
        }
    }
}
