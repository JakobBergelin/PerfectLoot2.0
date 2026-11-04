using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Poison Dart: a free repeated attack, fired like Fireball. A dart deals rolled weapon damage * WeaponDamageMultiplier as
/// poison and carries a PoisonDartHit, so AttackSystem marks what it hits (PoisonMark).
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial struct PoisonDartSystem : ISystem
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
        // SkillState: no cooldown to report (the time between attacks is the shared AttackRecovery); IsActive set by SkillActivationSystem
        var random = Unity.Mathematics.Random.CreateFromIndex(_frame++);
        float deltaTime = SystemAPI.Time.DeltaTime;
        var movementConfig = SystemAPI.GetSingleton<MovementConfig>();
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        // Casts requested by effects (e.g. Echoing Blade): no attack recovery
        foreach (var (dart, requests, skills, transform, stats, entity) in
                 SystemAPI.Query<RefRO<PoisonDart>, DynamicBuffer<SkillCastRequest>, DynamicBuffer<Skill>, RefRO<LocalTransform>, RefRO<CalculatedStats>>()
                          .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            while (SkillUtil.TryTakeRequest(requests, SkillType.PoisonDart, out var request))
            {
                Spawn(commandBuffer, entity, dart.ValueRO, stats.ValueRO.Value, ref random, transform.ValueRO.Position, request.Direction);
                SkillUtil.MarkUsed(skills, SkillType.PoisonDart, request.AutoCast);
            }
        }

        foreach (var (dart, skills, transform, desiredDirection, recovery, stats, entity) in
                 SystemAPI.Query<RefRO<PoisonDart>, DynamicBuffer<Skill>, RefRW<LocalTransform>, RefRO<DesiredDirection>, RefRW<AttackRecovery>, RefRO<CalculatedStats>>().WithEntityAccess())
        {
            bool facing = SkillUtil.TurnToward(ref transform.ValueRW, desiredDirection.ValueRO, movementConfig, deltaTime);
            if (recovery.ValueRO.Remaining > 0f || !facing) continue;

            // Fires as soon as it faces the cursor; the wait comes after (shared with the other repeated attacks)
            Spawn(commandBuffer, entity, dart.ValueRO, stats.ValueRO.Value, ref random, transform.ValueRO.Position, desiredDirection.ValueRO.Direction);
            recovery.ValueRW.Remaining += dart.ValueRO.AttackSpeed;
            SkillUtil.MarkUsed(skills, SkillType.PoisonDart);
        }
    }

    /// <summary>One dart from the caster toward direction.</summary>
    static void Spawn(EntityCommandBuffer commandBuffer, Entity caster, in PoisonDart dart, in StatBlock stats, ref Unity.Mathematics.Random random, float3 casterPosition, float3 direction)
    {
        float weaponDamage = stats.WeaponDamage + random.NextFloat(-stats.WeaponDamageRange, stats.WeaponDamageRange);
        var newEntity = commandBuffer.CreateEntity();

        commandBuffer.AddComponent(newEntity, new LocalTransform{Position = casterPosition + direction, Rotation = quaternion.identity, Scale = 0.5f});
        commandBuffer.AddComponent(newEntity, new Projectile{Direction = direction, Speed = dart.ProjectileSpeed, DestroyOnHit = true, BounceCount = 0, PierceCount = 0, FollowGround = true});
        commandBuffer.AddComponent(newEntity, new Attack{Damage = math.max(0f, weaponDamage * dart.WeaponDamageMultiplier), Type = DamageType.Poison, DamageHierarchy = DamageHierarchy.Main, Radius = dart.Radius, HitForce = dart.HitForce});
        commandBuffer.AddComponent(newEntity, new PoisonDartHit{MarkDuration = dart.MarkDuration, ExplosionDamage = dart.ExplosionDamage, ExplosionRadius = dart.ExplosionRadius});
        commandBuffer.AddComponent(newEntity, new Source{Entity = caster});
        commandBuffer.AddComponent(newEntity, new AttackTarget{TargetType = TargetType.Enemy});
        commandBuffer.AddComponent(newEntity, new InitializeFromSource{});
    }
}

/// <summary>
/// When a marked enemy dies, whatever killed it, it explodes: a normal poison attack by the dart's caster (damage %,
/// conversion, crits and area of effect apply) that can set off other marked enemies. Nothing happens if the caster is
/// gone. After the damage is dealt and before CheckDeathSystem removes the dead, while they still have the mark.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup), OrderLast = true)]
[UpdateAfter(typeof(DamagingSystem))]
[UpdateBefore(typeof(CheckDeathSystem))]
[BurstCompile]
public partial struct PoisonMarkExplosionSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        foreach (var (mark, health, transform, entity) in SystemAPI.Query<RefRO<PoisonMark>, RefRO<Health>, RefRO<LocalTransform>>()
                     .WithAll<Enemy>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            if (health.ValueRO.CurrentValue > 0f) continue;
            SystemAPI.SetComponentEnabled<PoisonMark>(entity, false);

            var caster = mark.ValueRO.Source;
            if (!SystemAPI.HasComponent<CalculatedStats>(caster)) continue;

            var newEntity = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(newEntity, new LocalTransform{Position = transform.ValueRO.Position, Rotation = quaternion.identity, Scale = 1f});
            commandBuffer.AddComponent(newEntity, new Attack{Damage = mark.ValueRO.ExplosionDamage, Type = DamageType.Poison, DamageHierarchy = DamageHierarchy.Main, Radius = mark.ValueRO.ExplosionRadius});
            commandBuffer.AddComponent(newEntity, new Source{Entity = caster});
            commandBuffer.AddComponent(newEntity, new AttackTarget{TargetType = TargetType.Enemy});
            commandBuffer.AddComponent(newEntity, new InitializeFromSource{});
            // single frame damage
            commandBuffer.AddComponent(newEntity, new DestroyWithChildren{});
        }
    }
}

/// <summary>Placeholder art: a green sphere over each marked enemy.</summary>
[UpdateInGroup(typeof(FeedbackSystemGroup))]
[BurstCompile]
partial struct PoisonMarkUISystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var transform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<PoisonMark, Enemy>())
            ProgrammerArt.DrawSphere(transform.ValueRO.Position + new float3(0f, 2f, 0f), 0.25f, Color.green);
    }
}
