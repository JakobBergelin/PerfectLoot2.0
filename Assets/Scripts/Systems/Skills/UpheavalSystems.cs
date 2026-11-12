using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Upheaval: once the caster faces the cursor, has recovered and can pay, it slams the ground at weapon reach in front of
/// it (a one-frame physical attack) and leaves a DelayedAttack on the same spot: QuakeDelay seconds later the ground heaves
/// in QuakeRadius around it, throwing enemies outward (HitForce pushes away from the attack). Both roll weapon damage at the
/// slam. Repeats while held, AttackSpeed apart (the shared AttackRecovery). Each slam is a Melee use.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial class UpheavalSystem : SystemBase
{
    private Random _random;
    private BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;

    [BurstCompile]
    protected override void OnCreate()
    {
        _random = Random.CreateFromIndex((uint)System.Environment.TickCount);
        _beginInitializationEntityCommandBufferSystem = World.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
        RequireForUpdate<MovementConfig>();
    }

    [BurstCompile]
    protected override void OnUpdate()
    {
        // SkillState: no cooldown to report (the time between slams is the shared AttackRecovery); IsActive/resource set by SkillActivationSystem
        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
        var movementConfig = SystemAPI.GetSingleton<MovementConfig>();

        foreach (var (upheaval, localTransform, desiredDirection, resource, stats, recovery, entity) in
                 SystemAPI.Query<RefRO<Upheaval>, RefRW<LocalTransform>, RefRO<DesiredDirection>, RefRO<Resource>, RefRO<CalculatedStats>, RefRW<AttackRecovery>>().WithEntityAccess())
        {
            bool facing = SkillUtil.TurnToward(ref localTransform.ValueRW, desiredDirection.ValueRO, movementConfig, SystemAPI.Time.DeltaTime);
            if (recovery.ValueRO.Remaining > 0f) continue;

            var u = upheaval.ValueRO;
            var statBlock = stats.ValueRO.Value;
            float cost = SkillUtil.Cost(u.ResourceCost, statBlock);
            if (!SkillUtil.CanAfford(resource.ValueRO.CurrentValue, cost, stats.ValueRO) || !facing) continue;

            float weaponDamage = statBlock.WeaponDamage + _random.NextFloat(-statBlock.WeaponDamageRange, statBlock.WeaponDamageRange);
            var direction = math.normalizesafe(new float3(desiredDirection.ValueRO.Direction.x, 0f, desiredDirection.ValueRO.Direction.z), new float3(0f, 0f, 1f));
            var spot = localTransform.ValueRO.Position + direction * statBlock.WeaponReach;
            var transform = new LocalTransform { Position = spot, Rotation = quaternion.identity, Scale = 1f };

            // The slam, this frame
            var slam = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(slam, transform);
            commandBuffer.AddComponent(slam, new Attack { Origin = DamageLabel.Upheaval, Damage = u.SlamDamage + weaponDamage * u.SlamWeaponDamageMultiplier, Type = DamageType.Physical, Radius = u.SlamRadius, DamageHierarchy = DamageHierarchy.Main });
            commandBuffer.AddComponent(slam, new Source { Entity = entity });
            commandBuffer.AddComponent(slam, new AttackTarget { TargetType = TargetType.Enemy });
            commandBuffer.AddComponent(slam, new InitializeFromSource());
            commandBuffer.AddComponent(slam, new DestroyWithChildren());   // A single-frame hit

            // The quake, on the same spot later
            var quake = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(quake, transform);
            commandBuffer.AddComponent(quake, new DelayedAttack
            {
                Attack = new Attack { Origin = DamageLabel.Upheaval, Damage = u.QuakeDamage + weaponDamage * u.QuakeWeaponDamageMultiplier, Type = DamageType.Physical, Radius = u.QuakeRadius, DamageHierarchy = DamageHierarchy.Main, HitForce = u.QuakeHitForce },
                TargetType = TargetType.Enemy,
                Delay = u.QuakeDelay,
            });
            commandBuffer.AddComponent(quake, new Source { Entity = entity });

            recovery.ValueRW.Remaining += u.AttackSpeed;
            SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger { Value = -cost });
            if (SystemAPI.HasBuffer<Skill>(entity)) SkillUtil.MarkUsed(SystemAPI.GetBuffer<Skill>(entity), SkillType.Upheaval);
        }
    }
}

/// <summary>
/// Counts delayed attacks down; one whose Delay has run out becomes a one-frame attack where it is (its source's stats
/// apply then, InitializeFromSource) and is removed after hitting.
/// </summary>
[UpdateInGroup(typeof(SkillSystemGroup))]
[BurstCompile]
public partial struct DelayedAttackSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<DelayedAttack>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        foreach (var (delayed, entity) in SystemAPI.Query<RefRW<DelayedAttack>>().WithEntityAccess())
        {
            delayed.ValueRW.Delay -= deltaTime;
            if (delayed.ValueRO.Delay > 0f) continue;
            commandBuffer.RemoveComponent<DelayedAttack>(entity);
            commandBuffer.AddComponent(entity, delayed.ValueRO.Attack);
            commandBuffer.AddComponent(entity, new AttackTarget { TargetType = delayed.ValueRO.TargetType });
            commandBuffer.AddComponent(entity, new InitializeFromSource());
            commandBuffer.AddComponent(entity, new DestroyWithChildren());   // A single-frame hit
        }
    }
}
