using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Multishot: once the caster faces the cursor, has recovered and can pay, it looses a volley: ArrowCount ground-following
/// arrows spread evenly over the cone toward the cursor, each rolling its own weapon damage. Repeats while held,
/// AttackSpeed apart (the shared AttackRecovery). Casts requested by effects (e.g. Echoing Blade) fire a volley the
/// requested way, with no attack recovery and the normal cost (skipped when it can't be paid) unless free.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial struct MultishotSystem : ISystem
{
    uint _frame;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<MovementConfig>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // SkillState: no cooldown to report (the time between volleys is the shared AttackRecovery); IsActive set by SkillActivationSystem
        var random = Random.CreateFromIndex(_frame++);
        float deltaTime = SystemAPI.Time.DeltaTime;
        var movementConfig = SystemAPI.GetSingleton<MovementConfig>();
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        foreach (var (multishot, requests, skills, resourceChanges, resource, transform, entity) in
                 SystemAPI.Query<RefRO<Multishot>, DynamicBuffer<SkillCastRequest>, DynamicBuffer<Skill>, DynamicBuffer<ResourceChanger>, RefRO<Resource>, RefRO<LocalTransform>>()
                          .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            var stats = SystemAPI.GetComponent<CalculatedStats>(entity);
            float available = resource.ValueRO.CurrentValue;
            float cost = SkillUtil.Cost(multishot.ValueRO.ResourceCost, stats.Value);
            while (SkillUtil.TryTakeRequest(requests, SkillType.Multishot, out var request))
            {
                if (!request.Free)
                {
                    if (!SkillUtil.CanAfford(available, cost, stats)) continue;
                    available = math.max(0f, available - cost);
                    resourceChanges.Add(new ResourceChanger { Value = -cost });
                }
                Volley(commandBuffer, entity, multishot.ValueRO, stats.Value, ref random, transform.ValueRO.Position, request.Direction);
                SkillUtil.MarkUsed(skills, SkillType.Multishot, request.AutoCast);
            }
        }

        foreach (var (multishot, skills, transform, desiredDirection, resource, recovery, entity) in
                 SystemAPI.Query<RefRO<Multishot>, DynamicBuffer<Skill>, RefRW<LocalTransform>, RefRO<DesiredDirection>, RefRO<Resource>, RefRW<AttackRecovery>>().WithEntityAccess())
        {
            bool facing = SkillUtil.TurnToward(ref transform.ValueRW, desiredDirection.ValueRO, movementConfig, deltaTime);
            if (recovery.ValueRO.Remaining > 0f || !facing) continue;

            var stats = SystemAPI.GetComponent<CalculatedStats>(entity);
            float cost = SkillUtil.Cost(multishot.ValueRO.ResourceCost, stats.Value);
            if (!SkillUtil.CanAfford(resource.ValueRO.CurrentValue, cost, stats)) continue;

            // Fires as soon as it faces the cursor; the wait comes after (shared with the other repeated attacks)
            Volley(commandBuffer, entity, multishot.ValueRO, stats.Value, ref random, transform.ValueRO.Position, desiredDirection.ValueRO.Direction);
            recovery.ValueRW.Remaining += multishot.ValueRO.AttackSpeed;
            SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger { Value = -cost });
            SkillUtil.MarkUsed(skills, SkillType.Multishot);
        }
    }

    /// <summary>The arrows, evenly from one edge of the cone to the other (a single arrow flies straight).</summary>
    static void Volley(EntityCommandBuffer commandBuffer, Entity caster, in Multishot multishot, in StatBlock stats, ref Random random, float3 casterPosition, float3 direction)
    {
        direction.y = 0f;
        direction = math.normalizesafe(direction, new float3(0f, 0f, 1f));
        int count = math.max(1, multishot.ArrowCount);
        float halfAngle = math.radians(multishot.HalfAngle);
        for (int i = 0; i < count; i++)
        {
            float angle = count == 1 ? 0f : math.lerp(-halfAngle, halfAngle, i / (float)(count - 1));
            var arrowDirection = math.rotate(quaternion.RotateY(angle), direction);
            float weaponDamage = stats.WeaponDamage + random.NextFloat(-stats.WeaponDamageRange, stats.WeaponDamageRange);
            var start = casterPosition + arrowDirection;

            var newEntity = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(newEntity, new LocalTransform { Position = start, Rotation = quaternion.LookRotationSafe(arrowDirection, math.up()), Scale = 0.5f });
            commandBuffer.AddComponent(newEntity, new Projectile { Direction = arrowDirection, Speed = multishot.ProjectileSpeed, DestroyOnHit = true, FollowGround = true });
            commandBuffer.AddComponent(newEntity, new ProjectileRange { MaxDistance = multishot.Range, LastPosition = start });
            commandBuffer.AddComponent(newEntity, new Attack
            {
                Origin = DamageLabel.Multishot,
                Damage = math.max(0f, weaponDamage * multishot.WeaponDamageMultiplier),
                Type = DamageType.Physical,
                DamageHierarchy = DamageHierarchy.Main,
                Radius = multishot.Radius,
                HitForce = multishot.HitForce
            });
            commandBuffer.AddComponent(newEntity, new Source { Entity = caster });
            commandBuffer.AddComponent(newEntity, new AttackTarget { TargetType = TargetType.Enemy });
            commandBuffer.AddComponent(newEntity, new InitializeFromSource());
        }
    }
}
