using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial class FrozenOrbSystem : SystemBase
{
    private BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;
    [BurstCompile]
    protected override void OnCreate()
    {
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
        foreach (var (orb, requests, skills, resourceChanges, resource, localTransform, stats, entity) in
                 SystemAPI.Query<RefRO<FrozenOrb>, DynamicBuffer<SkillCastRequest>, DynamicBuffer<Skill>, DynamicBuffer<ResourceChanger>, RefRO<Resource>, RefRO<LocalTransform>, RefRO<CalculatedStats>>()
                          .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            float available = resource.ValueRO.CurrentValue;
            float cost = SkillUtil.Cost(orb.ValueRO.ResourceCost, stats.ValueRO.Value);
            while (SkillUtil.TryTakeRequest(requests, SkillType.FrozenOrb, out var request))
            {
                if (!SkillUtil.CanAfford(available, cost, stats.ValueRO)) continue;
                available = Mathf.Max(0f, available - cost);
                Spawn(commandBuffer, entity, orb.ValueRO, localTransform.ValueRO.Position, request.Direction);
                resourceChanges.Add(new ResourceChanger{Value = -cost});
                SkillUtil.MarkUsed(skills, SkillType.FrozenOrb, request.AutoCast);
            }
        }

        foreach (var (orb, skillBuffer, localTransform, desiredDirection, resource, stats, recovery, entity) in SystemAPI.Query<RefRO<FrozenOrb>, DynamicBuffer<Skill>, RefRW<LocalTransform>, RefRO<DesiredDirection>, RefRO<Resource>, RefRO<CalculatedStats>, RefRW<AttackRecovery>>().WithEntityAccess())
        {
            localTransform.ValueRW.Rotation = Quaternion.RotateTowards(localTransform.ValueRO.Rotation, desiredDirection.ValueRO.Quaternion, movementConfig.RotationSpeed * SystemAPI.Time.DeltaTime);
            if(recovery.ValueRO.Remaining > 0f)
            {
                continue;
            }

            float cost = SkillUtil.Cost(orb.ValueRO.ResourceCost, stats.ValueRO.Value);
            if(!SkillUtil.CanAfford(resource.ValueRO.CurrentValue, cost, stats.ValueRO))
            {
                continue;
            }

            // Fires at once, facing the shot; the wait comes after (shared with the other repeated attacks)
            localTransform.ValueRW.Rotation = desiredDirection.ValueRO.Quaternion;
            Spawn(commandBuffer, entity, orb.ValueRO, localTransform.ValueRO.Position, desiredDirection.ValueRO.Direction);
            recovery.ValueRW.Remaining += orb.ValueRO.AttackSpeed;

            SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger{Value = -cost});
            SkillUtil.MarkUsed(skillBuffer, SkillType.FrozenOrb);
        }
        // Spawned orbs fly as projectiles (ProjectileSystem) and spin and fire shards (FrozenOrbSpinSystem)
    }

    /// <summary>
    /// One orb from the caster toward direction. Used by the normal cast and by effects that cast it (Echoing Blade);
    /// the caller pays the cost.
    /// </summary>
    public static void Spawn(EntityCommandBuffer commandBuffer, Entity caster, in FrozenOrb orb, float3 casterPosition, float3 direction)
    {
        var newEntity = commandBuffer.CreateEntity();

        // Starts spinning from the way it flies, so the first burst goes forward and back
        float startAngle = math.degrees(math.atan2(direction.x, direction.z));
        commandBuffer.AddComponent(newEntity, new LocalTransform{Position = casterPosition + direction, Rotation = quaternion.RotateY(math.radians(startAngle)), Scale = 1f});
        commandBuffer.AddComponent(newEntity, new Projectile{Direction = direction, Speed = orb.Speed, DestroyOnHit = false, FollowGround = true});
        commandBuffer.AddComponent(newEntity, new PeriodicAttack{DamagePerSecond = orb.DamagePerSecond, Type = DamageType.Cold, Radius = orb.Radius, TimeRemaining = orb.Lifetime, Skill = SkillType.FrozenOrb, TickRate = orb.TickRate});
        commandBuffer.AddComponent(newEntity, new FrozenOrbProjectile
        {
            SpinSpeed = orb.SpinSpeed, SpinAngle = startAngle, ShardInterval = orb.ShardInterval,
            ShardDamage = orb.ShardDamage, ShardSpeed = orb.ShardSpeed, ShardRadius = orb.ShardRadius, ShardRange = orb.ShardRange,
        });
        commandBuffer.AddComponent(newEntity, new Source{Entity = caster});
        commandBuffer.AddComponent(newEntity, new AttackTarget{TargetType = TargetType.Enemy});
        commandBuffer.AddComponent(newEntity, new InitializeFromSource{});
    }
}

/// <summary>
/// Spins Frozen Orbs and, every ShardInterval, fires an ice shard (IceShard) straight out from the orb's front edge and
/// one from its back edge. As the orb turns between bursts, the shards spiral out.
/// </summary>
[UpdateInGroup(typeof(SkillSystemGroup))]
[BurstCompile]
public partial struct FrozenOrbSpinSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<FrozenOrbProjectile>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        foreach (var (orb, transform, source) in SystemAPI.Query<RefRW<FrozenOrbProjectile>, RefRW<LocalTransform>, RefRO<Source>>().WithNone<DestroyWithChildren>())
        {
            ref var o = ref orb.ValueRW;
            o.SpinAngle = math.fmod(o.SpinAngle + o.SpinSpeed * deltaTime, 360f);
            var rotation = quaternion.RotateY(math.radians(o.SpinAngle));
            transform.ValueRW.Rotation = rotation;

            if (o.ShardInterval <= 0f) continue;
            o.ShardElapsed += deltaTime;
            if (o.ShardElapsed < o.ShardInterval) continue;
            o.ShardElapsed %= o.ShardInterval;

            var front = math.rotate(rotation, new float3(0f, 0f, 1f));
            float edge = transform.ValueRO.Scale * 0.5f;
            var center = transform.ValueRO.Position;
            IceShard.Spawn(commandBuffer, source.ValueRO.Entity, o.ShardDamage, o.ShardSpeed, o.ShardRadius, o.ShardRange, center + front * edge, front);
            IceShard.Spawn(commandBuffer, source.ValueRO.Entity, o.ShardDamage, o.ShardSpeed, o.ShardRadius, o.ShardRange, center - front * edge, -front);
        }
    }
}
