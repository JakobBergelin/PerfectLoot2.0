using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Plague Arrow: fires a slow arrow toward the cursor once the caster faces it, then waits its AttackSpeed (the shared
/// AttackRecovery). The arrow is a projectile with a periodic poison attack around it. It passes through walls and is
/// removed after exactly Range meters of flight (ProjectileRange), so its periodic attack has no time limit of its own:
/// the caster's projectile speed (applied when it spawns, InitializeFromSource) decides how long it poisons. Casts
/// requested by effects (Echoing Blade) work like Frozen Orb's.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial class PlagueArrowSystem : SystemBase
{
    private BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;

    [BurstCompile]
    protected override void OnCreate()
    {
        _beginInitializationEntityCommandBufferSystem = World.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
        RequireForUpdate<MovementConfig>();
    }

    [BurstCompile]
    protected override void OnUpdate()
    {
        // SkillState: no cooldown to report (the time between attacks is the shared AttackRecovery); IsActive/resource set by SkillActivationSystem
        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
        var movementConfig = SystemAPI.GetSingleton<MovementConfig>();

        // Casts requested by effects: no attack recovery, the normal cost (skipped if it can't be paid) unless free
        foreach (var (arrow, requests, skills, resourceChanges, resource, localTransform, stats, entity) in
                 SystemAPI.Query<RefRO<PlagueArrow>, DynamicBuffer<SkillCastRequest>, DynamicBuffer<Skill>, DynamicBuffer<ResourceChanger>, RefRO<Resource>, RefRO<LocalTransform>, RefRO<CalculatedStats>>()
                          .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            float available = resource.ValueRO.CurrentValue;
            float cost = SkillUtil.Cost(arrow.ValueRO.ResourceCost, stats.ValueRO.Value);
            while (SkillUtil.TryTakeRequest(requests, SkillType.PlagueArrow, out var request))
            {
                if (!request.Free)
                {
                    if (!SkillUtil.CanAfford(available, cost, stats.ValueRO)) continue;
                    available = Mathf.Max(0f, available - cost);
                    resourceChanges.Add(new ResourceChanger{Value = -cost});
                }
                Spawn(commandBuffer, entity, arrow.ValueRO, localTransform.ValueRO.Position, request.Direction);
                SkillUtil.MarkUsed(skills, SkillType.PlagueArrow, request.AutoCast);
            }
        }

        foreach (var (arrow, skillBuffer, localTransform, desiredDirection, resource, stats, recovery, entity) in
                 SystemAPI.Query<RefRO<PlagueArrow>, DynamicBuffer<Skill>, RefRW<LocalTransform>, RefRO<DesiredDirection>, RefRO<Resource>, RefRO<CalculatedStats>, RefRW<AttackRecovery>>().WithEntityAccess())
        {
            bool facing = SkillUtil.TurnToward(ref localTransform.ValueRW, desiredDirection.ValueRO, movementConfig, SystemAPI.Time.DeltaTime);
            if (recovery.ValueRO.Remaining > 0f) continue;

            float cost = SkillUtil.Cost(arrow.ValueRO.ResourceCost, stats.ValueRO.Value);
            if (!SkillUtil.CanAfford(resource.ValueRO.CurrentValue, cost, stats.ValueRO)) continue;
            if (!facing) continue;

            // Fires as soon as it faces the cursor; the wait comes after (shared with the other repeated attacks)
            Spawn(commandBuffer, entity, arrow.ValueRO, localTransform.ValueRO.Position, desiredDirection.ValueRO.Direction);
            recovery.ValueRW.Remaining += arrow.ValueRO.AttackSpeed;
            SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger{Value = -cost});
            SkillUtil.MarkUsed(skillBuffer, SkillType.PlagueArrow);
        }
    }

    /// <summary>One arrow from the caster toward direction. The caller pays the cost.</summary>
    public static void Spawn(EntityCommandBuffer commandBuffer, Entity caster, in PlagueArrow arrow, float3 casterPosition, float3 direction)
    {
        var start = casterPosition + direction;
        var newEntity = commandBuffer.CreateEntity();
        commandBuffer.AddComponent(newEntity, new LocalTransform{Position = start, Rotation = quaternion.LookRotationSafe(direction, math.up()), Scale = 1f});
        commandBuffer.AddComponent(newEntity, new Projectile{Direction = direction, Speed = arrow.Speed, DestroyOnHit = false, FollowGround = true, PassThroughWalls = true});
        commandBuffer.AddComponent(newEntity, new ProjectileRange{MaxDistance = arrow.Range, LastPosition = start});
        // No time limit: the arrow ends after Range meters (see the summary)
        commandBuffer.AddComponent(newEntity, new PeriodicAttack{Origin = DamageLabel.PlagueArrow, DamagePerSecond = arrow.DamagePerSecond, Type = DamageType.Poison, Radius = arrow.Radius, TimeRemaining = float.MaxValue, Skill = SkillType.PlagueArrow, TickRate = arrow.TickRate});
        commandBuffer.AddComponent(newEntity, new Source{Entity = caster});
        commandBuffer.AddComponent(newEntity, new AttackTarget{TargetType = TargetType.Enemy});
        commandBuffer.AddComponent(newEntity, new InitializeFromSource{});
    }
}
