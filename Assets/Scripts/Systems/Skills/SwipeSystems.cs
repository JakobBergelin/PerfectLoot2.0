using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Swipe: once the caster faces the cursor and has recovered, it swings: a hit circle that sweeps an arc in front of it
/// (SweepingAttack), right to left, then left to right on the next swing. Letting go keeps the side: the next attack
/// swings back from where the last one ended. Damage is Damage + weapon damage * WeaponDamageMultiplier. The arc is twice the weapon reach out, and the hit
/// circle counts the area of effect bonus twice (so it's marked OwnAreaScaling). Each swing is a Melee use.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial class SwipeSystem : SystemBase
{
    private Unity.Mathematics.Random _random;
    private BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;

    [BurstCompile]
    protected override void OnCreate()
    {
        _random = Unity.Mathematics.Random.CreateFromIndex((uint)System.Environment.TickCount);
        _beginInitializationEntityCommandBufferSystem = World.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
        RequireForUpdate<MovementConfig>();
    }

    [BurstCompile]
    protected override void OnUpdate()
    {
        // SkillState: no cooldown to report (the time between swings is the shared AttackRecovery)
        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
        var movementConfig = SystemAPI.GetSingleton<MovementConfig>();

        foreach (var (swipe, localTransform, desiredDirection, stats, recovery, entity) in
                 SystemAPI.Query<RefRW<Swipe>, RefRW<LocalTransform>, RefRO<DesiredDirection>, RefRO<CalculatedStats>, RefRW<AttackRecovery>>().WithEntityAccess())
        {
            bool facing = SkillUtil.TurnToward(ref localTransform.ValueRW, desiredDirection.ValueRO, movementConfig, SystemAPI.Time.DeltaTime);
            if (recovery.ValueRO.Remaining > 0f || !facing) continue;

            ref var s = ref swipe.ValueRW;
            var statBlock = stats.ValueRO.Value;
            float weaponDamage = statBlock.WeaponDamage + _random.NextFloat(-statBlock.WeaponDamageRange, statBlock.WeaponDamageRange);
            float damage = s.Damage + weaponDamage * s.WeaponDamageMultiplier;
            float area = 1f + 2f * (statBlock.AreaMultiplier - 1f);     // The area of effect bonus, twice
            float reach = 2f * statBlock.WeaponReach;                   // The weapon reach, twice

            // Even swings right to left, odd ones left to right (degrees from facing, positive to the right)
            bool rightToLeft = s.SwingCount % 2 == 0;
            float start = rightToLeft ? s.ArcAngle : -s.ArcAngle;
            var forward = math.normalizesafe(new float3(desiredDirection.ValueRO.Direction.x, 0f, desiredDirection.ValueRO.Direction.z), new float3(0f, 0f, 1f));
            var origin = localTransform.ValueRO.Position;

            var newEntity = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(newEntity, new LocalTransform{Position = origin + math.mul(quaternion.RotateY(math.radians(start)), forward) * reach, Rotation = quaternion.identity, Scale = 1f});
            commandBuffer.AddComponent(newEntity, new Attack{Origin = DamageLabel.Swipe, Damage = damage, Type = DamageType.Physical, Radius = s.Radius * math.max(0f, area), DamageHierarchy = DamageHierarchy.Main, HitForce = s.HitForce, GracePeriod = s.SweepDuration + 0.1f});
            commandBuffer.AddComponent(newEntity, new SweepingAttack{Center = entity, Forward = forward, StartAngle = start, EndAngle = -start, Distance = reach, Duration = s.SweepDuration});
            commandBuffer.AddComponent(newEntity, new OwnAreaScaling());
            commandBuffer.AddComponent(newEntity, new Source{Entity = entity});
            commandBuffer.AddComponent(newEntity, new AttackTarget{TargetType = TargetType.Enemy});
            commandBuffer.AddComponent(newEntity, new InitializeFromSource());

            s.SwingCount++;
            recovery.ValueRW.Remaining += s.AttackSpeed;
            // Each swing is a use (tagged Melee, so it also raises MeleeAttackUsed)
            if (SystemAPI.HasBuffer<Skill>(entity)) SkillUtil.MarkUsed(SystemAPI.GetBuffer<Skill>(entity), SkillType.Swipe);
        }
    }
}

/// <summary>
/// Moves sweeping attacks along their arc around their Center (following it as it moves) and removes them once the sweep
/// is over, or when the Center is gone. Before attacks hit, so the hit circle is where the blade is this frame.
/// </summary>
[UpdateInGroup(typeof(MovementSystemGroup))]
[BurstCompile]
public partial struct SweepingAttackSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var transforms = SystemAPI.GetComponentLookup<LocalTransform>(true);

        foreach (var (sweep, transform, entity) in SystemAPI.Query<RefRW<SweepingAttack>, RefRW<LocalTransform>>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            ref var s = ref sweep.ValueRW;
            if (!transforms.HasComponent(s.Center) || s.Elapsed >= s.Duration)
            {
                commandBuffer.AddComponent(entity, new DestroyWithChildren());
                continue;
            }
            s.Elapsed += deltaTime;
            float angle = math.lerp(s.StartAngle, s.EndAngle, math.saturate(s.Elapsed / math.max(s.Duration, 1e-4f)));
            var direction = math.mul(quaternion.RotateY(math.radians(angle)), s.Forward);
            transform.ValueRW.Position = transforms[s.Center].Position + direction * s.Distance;
            transform.ValueRW.Rotation = quaternion.LookRotationSafe(direction, math.up());
        }
    }
}

/// <summary>Swipe: every enemy a swing hits gives its caster ResourcePerHit. Between working out the damage and dealing it.</summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateBefore(typeof(DamagingSystem))]
[BurstCompile]
public partial struct SwipeResourceSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Swipe>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var swipes = SystemAPI.GetComponentLookup<Swipe>(true);
        var resourceChanges = SystemAPI.GetBufferLookup<ResourceChanger>();
        foreach (var taken in SystemAPI.Query<DynamicBuffer<CalculatedDamageInstance>>().WithAll<Enemy>())
            foreach (var instance in taken)
                if (instance.Origin.Label == DamageLabel.Swipe && swipes.HasComponent(instance.Source) && resourceChanges.HasBuffer(instance.Source))
                    resourceChanges[instance.Source].Add(new ResourceChanger { Value = swipes[instance.Source].ResourcePerHit });
    }
}
