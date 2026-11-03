using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial class FireballSystem : SystemBase
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
        foreach (var (fireball, requests, skills, resourceChanges, resource, localTransform, entity) in
                 SystemAPI.Query<RefRO<FireBall>, DynamicBuffer<SkillCastRequest>, DynamicBuffer<Skill>, DynamicBuffer<ResourceChanger>, RefRO<Resource>, RefRO<LocalTransform>>()
                          .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            float available = resource.ValueRO.CurrentValue;
            var stats = SystemAPI.GetComponent<CalculatedStats>(entity);
            float cost = SkillUtil.Cost(fireball.ValueRO.ResourceCost, stats.Value);
            while (SkillUtil.TryTakeRequest(requests, SkillType.Fireball, out var request))
            {
                if (!SkillUtil.CanAfford(available, cost, stats)) continue;
                available = Mathf.Max(0f, available - cost);
                Spawn(commandBuffer, entity, fireball.ValueRO, localTransform.ValueRO.Position, request.Direction);
                resourceChanges.Add(new ResourceChanger{Value = -cost});
                SkillUtil.MarkUsed(skills, SkillType.Fireball, request.AutoCast);
            }
        }

        foreach (var (fireball, skillBuffer, localTransform, desiredDirection, resource, recovery, entity) in SystemAPI.Query<RefRO<FireBall>, DynamicBuffer<Skill>, RefRW<LocalTransform>, RefRO<DesiredDirection>, RefRO<Resource>, RefRW<AttackRecovery>>().WithEntityAccess())
        {
            localTransform.ValueRW.Rotation = Quaternion.RotateTowards(localTransform.ValueRO.Rotation, desiredDirection.ValueRO.Quaternion, movementConfig.RotationSpeed * SystemAPI.Time.DeltaTime);
            if(recovery.ValueRO.Remaining > 0f)
            {
                continue;
            }

            var stats = SystemAPI.GetComponent<CalculatedStats>(entity);
            float cost = SkillUtil.Cost(fireball.ValueRO.ResourceCost, stats.Value);
            if(!SkillUtil.CanAfford(resource.ValueRO.CurrentValue, cost, stats))
            {
                continue;
            }

            // Fires at once, facing the shot; the wait comes after (shared with the other repeated attacks)
            localTransform.ValueRW.Rotation = desiredDirection.ValueRO.Quaternion;
            Spawn(commandBuffer, entity, fireball.ValueRO, localTransform.ValueRO.Position, desiredDirection.ValueRO.Direction);
            recovery.ValueRW.Remaining += fireball.ValueRO.AttackSpeed;

            SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger{Value = -cost});
            SkillUtil.MarkUsed(skillBuffer, SkillType.Fireball);
        }
    }

    /// <summary>
    /// One fireball from the caster toward direction. Used by the normal cast and by effects that cast it (Echoing Blade);
    /// the caller pays the cost.
    /// </summary>
    public static void Spawn(EntityCommandBuffer commandBuffer, Entity caster, in FireBall fireball, float3 casterPosition, float3 direction)
    {
        var newEntity = commandBuffer.CreateEntity();

        commandBuffer.AddComponent(newEntity, new LocalTransform{Position = casterPosition + direction, Rotation = quaternion.identity, Scale = 0.5f});
        commandBuffer.AddComponent(newEntity, new Projectile{Direction = direction, Speed = fireball.ProjectileSpeed, DestroyOnHit = true, BounceCount = 0, PierceCount = 0, FollowGround = true});
        commandBuffer.AddComponent(newEntity, new Attack{Damage = fireball.Damage, Type = DamageType.Fire, DamageHierarchy = DamageHierarchy.Main, Radius = fireball.Radius, HitForce = fireball.HitForce});
        commandBuffer.AddComponent(newEntity, new Source{Entity = caster});
        commandBuffer.AddComponent(newEntity, new AttackTarget{TargetType = TargetType.Enemy});
        commandBuffer.AddComponent(newEntity, new InitializeFromSource{});
    }
}
