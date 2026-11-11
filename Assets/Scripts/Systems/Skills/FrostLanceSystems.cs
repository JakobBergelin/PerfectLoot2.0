using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Frost Lance: once the caster faces the cursor and has recovered, it thrusts: a thin rectangle (AttackShape) from the
/// weapon's reach to Length meters past it, for one frame, hitting everything on it once for weapon damage *
/// WeaponDamageMultiplier cold. Area of effect makes the line longer and wider, still starting at the weapon's reach (so
/// it's marked OwnAreaScaling). Each thrust builds ResourcePerAttack and grants the Frost Lance buff for BuffDuration
/// (scaled by the buff duration stat, restarting it). Each thrust is a Melee use.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial class FrostLanceSystem : SystemBase
{
    Random _random;
    BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;

    protected override void OnCreate()
    {
        _random = Random.CreateFromIndex((uint)System.Environment.TickCount);
        _beginInitializationEntityCommandBufferSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
        RequireForUpdate<MovementConfig>();
    }

    protected override void OnUpdate()
    {
        // SkillState: no cooldown to report (the time between thrusts is the shared AttackRecovery)
        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
        var movementConfig = SystemAPI.GetSingleton<MovementConfig>();

        foreach (var (lance, localTransform, desiredDirection, stats, recovery, entity) in
                 SystemAPI.Query<RefRO<FrostLance>, RefRW<LocalTransform>, RefRO<DesiredDirection>, RefRO<CalculatedStats>, RefRW<AttackRecovery>>().WithEntityAccess())
        {
            bool facing = SkillUtil.TurnToward(ref localTransform.ValueRW, desiredDirection.ValueRO, movementConfig, SystemAPI.Time.DeltaTime);
            if (recovery.ValueRO.Remaining > 0f || !facing) continue;

            var l = lance.ValueRO;
            var statBlock = stats.ValueRO.Value;
            float weaponDamage = statBlock.WeaponDamage + _random.NextFloat(-statBlock.WeaponDamageRange, statBlock.WeaponDamageRange);
            float area = math.max(0f, statBlock.AreaMultiplier);
            float halfLength = l.Length * 0.5f * area;
            var forward = math.normalizesafe(new float3(desiredDirection.ValueRO.Direction.x, 0f, desiredDirection.ValueRO.Direction.z), new float3(0f, 0f, 1f));

            // The line's middle: past the weapon's reach by half its length
            var newEntity = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(newEntity, new LocalTransform
            {
                Position = localTransform.ValueRO.Position + forward * (statBlock.WeaponReach + halfLength),
                Rotation = quaternion.LookRotationSafe(forward, math.up()),
                Scale = 1f
            });
            commandBuffer.AddComponent(newEntity, new Attack
            {
                Origin = DamageLabel.FrostLance,
                Damage = math.max(0f, weaponDamage * l.WeaponDamageMultiplier),
                Type = DamageType.Cold,
                Radius = halfLength,
                DamageHierarchy = DamageHierarchy.Main,
                HitForce = l.HitForce
            });
            commandBuffer.AddComponent(newEntity, new AttackShape
            {
                Kind = AttackShapeKind.Rectangle,
                HalfExtents = new float3(l.Width * 0.5f * area, l.Height * 0.5f, halfLength)
            });
            commandBuffer.AddComponent(newEntity, new OwnAreaScaling());
            commandBuffer.AddComponent(newEntity, new Source { Entity = entity });
            commandBuffer.AddComponent(newEntity, new AttackTarget { TargetType = TargetType.Enemy });
            commandBuffer.AddComponent(newEntity, new InitializeFromSource());
            // Single frame damage
            commandBuffer.AddComponent(newEntity, new DestroyWithChildren());

            recovery.ValueRW.Remaining += l.AttackSpeed;
            SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger { Value = l.ResourcePerAttack });

            // The buff, restarted (none with a buff duration of -100% or less)
            float duration = EffectApply.BuffDuration(l.BuffDuration, statBlock);
            if (duration > 0f && SystemAPI.HasComponent<FrostLanceBuff>(entity))
            {
                SystemAPI.SetComponent(entity, new FrostLanceBuff { ProjectileDamageReduction = l.ProjectileDamageReduction, TimeRemaining = duration, Duration = duration });
                SystemAPI.SetComponentEnabled<FrostLanceBuff>(entity, true);
            }

            // Each thrust is a use (tagged Melee, so it also raises MeleeAttackUsed)
            if (SystemAPI.HasBuffer<Skill>(entity)) SkillUtil.MarkUsed(SystemAPI.GetBuffer<Skill>(entity), SkillType.FrostLance);
        }
    }
}

/// <summary>Adds the Frost Lance buff's projectile damage reduction while it is active.</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct FrostLanceBuffStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (buff, stats) in SystemAPI.Query<RefRO<FrostLanceBuff>, RefRW<CalculatedStats>>())
            stats.ValueRW.Value.ProjectileDamageReduction += buff.ValueRO.ProjectileDamageReduction;
    }
}
