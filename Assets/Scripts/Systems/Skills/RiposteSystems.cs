using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Riposte: when off cooldown and facing the cursor, a one-frame physical hit in a cone in front of the caster (weapon
/// reach long, scaled by area of effect like any attack) for weapon damage * WeaponDamageMultiplier, knocking enemies far
/// back (HitForce, away from the cone's centre line). It opens the deflect window (RiposteDeflectSystem) for Window
/// seconds. Reports the cooldown, and is active while the window is open.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial class RiposteSystem : SystemBase
{
    Random _random;
    BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;

    protected override void OnCreate()
    {
        _random = Random.CreateFromIndex((uint)System.Environment.TickCount);
        _beginInitializationEntityCommandBufferSystem = World.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
        RequireForUpdate<MovementConfig>();
    }

    protected override void OnUpdate()
    {
        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
        var movementConfig = SystemAPI.GetSingleton<MovementConfig>();
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (riposte, stats) in SystemAPI.Query<RefRW<Riposte>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            ref var r = ref riposte.ValueRW;
            r.Elapsed = math.min(r.Elapsed + deltaTime, SkillUtil.Cooldown(r.Cooldown, stats.ValueRO.Value));
            r.WindowRemaining = math.max(0f, r.WindowRemaining - deltaTime);
        }

        foreach (var (riposte, localTransform, desiredDirection, stats, skills, entity) in
                 SystemAPI.Query<RefRW<Riposte>, RefRW<LocalTransform>, RefRO<DesiredDirection>, RefRO<CalculatedStats>, DynamicBuffer<Skill>>().WithEntityAccess())
        {
            ref var r = ref riposte.ValueRW;
            var statBlock = stats.ValueRO.Value;
            bool facing = SkillUtil.TurnToward(ref localTransform.ValueRW, desiredDirection.ValueRO, movementConfig, deltaTime);
            if (r.Elapsed < SkillUtil.Cooldown(r.Cooldown, statBlock) || !facing) continue;

            float weaponDamage = statBlock.WeaponDamage + _random.NextFloat(-statBlock.WeaponDamageRange, statBlock.WeaponDamageRange);
            var forward = AimDirection(desiredDirection.ValueRO);
            var newEntity = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(newEntity, new LocalTransform { Position = localTransform.ValueRO.Position, Rotation = quaternion.LookRotationSafe(forward, math.up()), Scale = 1f });
            commandBuffer.AddComponent(newEntity, new Attack
            {
                Origin = DamageLabel.Riposte,
                Damage = math.max(0f, weaponDamage * r.WeaponDamageMultiplier),
                Type = DamageType.Physical,
                Radius = statBlock.WeaponReach,
                DamageHierarchy = DamageHierarchy.Main,
                HitForce = r.HitForce
            });
            commandBuffer.AddComponent(newEntity, new AttackShape { Kind = AttackShapeKind.Cone, HalfAngle = r.HalfAngle });
            commandBuffer.AddComponent(newEntity, new Source { Entity = entity });
            commandBuffer.AddComponent(newEntity, new AttackTarget { TargetType = TargetType.Enemy });
            commandBuffer.AddComponent(newEntity, new InitializeFromSource());
            commandBuffer.AddComponent(newEntity, new DestroyWithChildren());   // A single-frame hit

            r.Elapsed = 0f;
            r.WindowRemaining = r.Window;
            SkillUtil.MarkUsed(skills, SkillType.Riposte);
        }

        // Report cooldown (after SkillActivationSystem's defaults)
        foreach (var (riposte, skillBuffer, stats) in SystemAPI.Query<RefRW<Riposte>, DynamicBuffer<Skill>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            int index = -1;
            if (!SkillUtil.FindSkill(skillBuffer, SkillType.Riposte, ref index)) continue;
            ref var skill = ref skillBuffer.ElementAt(index);
            SkillUtil.ApplyCooldownReduction(ref skill, ref riposte.ValueRW.Elapsed);
            float cooldown = SkillUtil.Cooldown(riposte.ValueRO.Cooldown, stats.ValueRO.Value);
            skill.State.CooldownDuration = cooldown;
            skill.State.CooldownRemaining = math.max(0f, cooldown - riposte.ValueRO.Elapsed);
            skill.State.IsActive = riposte.ValueRO.WindowRemaining > 0f;
        }
    }

    /// <summary>Where the caster aims, flat.</summary>
    public static float3 AimDirection(in DesiredDirection desiredDirection) =>
        math.normalizesafe(new float3(desiredDirection.Direction.x, 0f, desiredDirection.Direction.z), new float3(0f, 0f, 1f));
}

/// <summary>
/// While a Riposte window is open, every enemy projectile inside its cone (in front of the caster, along where it aims
/// now, weapon reach scaled by area of effect, any height) becomes the caster's (ProjectileUtil.TakeOver) and flies the
/// way the caster aims. Homing ones stop homing.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateAfter(typeof(ProjectileSystem))]
public partial struct RiposteDeflectSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Riposte>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var cones = new NativeList<LocalTransform>(Allocator.Temp);
        var shapes = new NativeList<AttackShape>(Allocator.Temp);
        var reaches = new NativeList<float>(Allocator.Temp);
        var casters = new NativeList<Entity>(Allocator.Temp);
        foreach (var (riposte, transform, desiredDirection, stats, entity) in
                 SystemAPI.Query<RefRO<Riposte>, RefRO<LocalTransform>, RefRO<DesiredDirection>, RefRO<CalculatedStats>>()
                          .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            if (riposte.ValueRO.WindowRemaining <= 0f) continue;
            var statBlock = stats.ValueRO.Value;
            var aim = RiposteSystem.AimDirection(desiredDirection.ValueRO);
            cones.Add(new LocalTransform { Position = transform.ValueRO.Position, Rotation = quaternion.LookRotationSafe(aim, math.up()), Scale = 1f });
            shapes.Add(new AttackShape { Kind = AttackShapeKind.Cone, HalfAngle = riposte.ValueRO.HalfAngle });
            reaches.Add(statBlock.WeaponReach * math.max(0f, statBlock.AreaMultiplier));
            casters.Add(entity);
        }
        if (cones.IsEmpty) return;

        var commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        foreach (var (target, transform, projectile, attack, source, entity) in
                 SystemAPI.Query<RefRW<AttackTarget>, RefRW<LocalTransform>, RefRW<Projectile>, RefRW<Attack>, RefRW<Source>>()
                     .WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            if (target.ValueRO.TargetType != TargetType.Player) continue;
            for (int i = 0; i < cones.Length; i++)
            {
                if (!AttackShapes.Contains(cones[i], reaches[i], true, shapes[i], transform.ValueRO.Position)) continue;

                bool hasRange = SystemAPI.HasComponent<ProjectileRange>(entity);
                var range = hasRange ? SystemAPI.GetComponent<ProjectileRange>(entity) : default;
                ProjectileUtil.TakeOver(ref attack.ValueRW, ref source.ValueRW, ref target.ValueRW, ref range, hasRange, transform.ValueRO.Position, casters[i], DamageLabel.Riposte);
                if (hasRange) SystemAPI.SetComponent(entity, range);

                var aim = math.mul(cones[i].Rotation, new float3(0f, 0f, 1f));
                projectile.ValueRW.Direction = aim;
                transform.ValueRW.Rotation = cones[i].Rotation;
                if (SystemAPI.HasComponent<HomingProjectile>(entity)) commandBuffer.RemoveComponent<HomingProjectile>(entity);
                break;
            }
        }
    }
}
