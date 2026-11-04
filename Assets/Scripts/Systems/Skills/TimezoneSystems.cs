using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

/// <summary>Timezone: when off cooldown, leaves a TimezoneField where the caster stands. The field stays there.</summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial struct TimezoneSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        foreach (var (timezone, stats) in SystemAPI.Query<RefRW<Timezone>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
            timezone.ValueRW.Elapsed = math.min(timezone.ValueRO.Elapsed + deltaTime, SkillUtil.Cooldown(timezone.ValueRO.Cooldown, stats.ValueRO.Value));

        foreach (var (timezone, transform, skillBuffer, stats) in SystemAPI.Query<RefRW<Timezone>, RefRO<LocalTransform>, DynamicBuffer<Skill>, RefRO<CalculatedStats>>())
        {
            ref var t = ref timezone.ValueRW;
            var statBlock = stats.ValueRO.Value;
            if (t.Elapsed < SkillUtil.Cooldown(t.Cooldown, statBlock)) continue;
            t.Elapsed = 0f;

            var newEntity = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(newEntity, new LocalTransform{Position = transform.ValueRO.Position, Rotation = quaternion.identity, Scale = 1f});
            commandBuffer.AddComponent(newEntity, new TimezoneField{Radius = t.Radius * statBlock.AreaMultiplier, TimeRemaining = t.Duration, ChillPerTick = t.ChillPerTick, ProjectileSlow = t.ProjectileSlow});

            SkillUtil.MarkUsed(skillBuffer, SkillType.Timezone);
        }

        // Report cooldown (after SkillActivationSystem's defaults)
        foreach (var (timezone, skillBuffer, stats) in SystemAPI.Query<RefRW<Timezone>, DynamicBuffer<Skill>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            int index = -1;
            if (SkillUtil.FindSkill(skillBuffer, SkillType.Timezone, ref index))
            {
                ref var skill = ref skillBuffer.ElementAt(index);
                SkillUtil.ApplyCooldownReduction(ref skill, ref timezone.ValueRW.Elapsed);
                float cooldown = SkillUtil.Cooldown(timezone.ValueRO.Cooldown, stats.ValueRO.Value);
                skill.State.CooldownDuration = cooldown;
                skill.State.CooldownRemaining = math.max(0f, cooldown - timezone.ValueRO.Elapsed);
            }
        }
    }
}

/// <summary>
/// Timezone fields: every TickInterval, each enemy inside gets chill, so one that stays inside freezes (ChillSystem).
/// Every frame, each projectile inside gets Projectile.Slow, whoever fired it. Fields reach any height for projectiles,
/// so high-flying ones count too. Overlapping fields count once. Runs every frame, also without fields, so projectiles
/// get their speed back when the last field ends. Counts fields down and removes them.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateBefore(typeof(ShieldProjectileSystem))]
[UpdateBefore(typeof(ProjectileSystem))]
public partial struct TimezoneFieldSystem : ISystem
{
    public const float TickInterval = 0.5f;

    float _tickTimer;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _tickTimer = TickInterval;
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<StatusEffectConfig>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        var commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        var fields = new NativeList<ActiveField>(Allocator.Temp);
        foreach (var (field, transform, entity) in SystemAPI.Query<RefRW<TimezoneField>, RefRO<LocalTransform>>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            field.ValueRW.TimeRemaining -= deltaTime;
            if (field.ValueRO.TimeRemaining <= 0f) commandBuffer.AddComponent<DestroyWithChildren>(entity);
            else fields.Add(new ActiveField { Field = field.ValueRO, Position = transform.ValueRO.Position });
        }

        _tickTimer -= deltaTime;
        if (_tickTimer <= 0f)
        {
            _tickTimer = math.max(_tickTimer + TickInterval, 0f);
            if (!fields.IsEmpty) Chill(ref state, fields);
        }

        foreach (var (projectile, transform) in SystemAPI.Query<RefRW<Projectile>, RefRO<LocalTransform>>())
        {
            float slow = 0f;
            foreach (var active in fields)
            {
                float radius = active.Field.Radius;
                if (math.distancesq(transform.ValueRO.Position.xz, active.Position.xz) <= radius * radius)
                    slow = math.max(slow, active.Field.ProjectileSlow);
            }
            projectile.ValueRW.Slow = slow;
        }
    }

    void Chill(ref SystemState state, NativeList<ActiveField> fields)
    {
        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        var settings = SystemAPI.GetSingleton<StatusEffectConfig>();
        var chilledLookup = SystemAPI.GetComponentLookup<Chilled>();
        state.CompleteDependency();  // The lookup is used here on the main thread; TimedEffectSystem ticks it in a job

        var hits = new NativeList<DistanceHit>(Allocator.Temp);
        var chilled = new NativeHashSet<Entity>(16, Allocator.Temp);
        var filter = new CollisionFilter
        {
            BelongsTo = ~0u,
            CollidesWith = (uint)CollisionLayerHelper.AnyEnemy,
            GroupIndex = 0,
        };

        foreach (var active in fields)
        {
            hits.Clear();
            physicsWorld.OverlapSphere(active.Position, active.Field.Radius, ref hits, filter);
            foreach (var hit in hits)
            {
                if (!SystemAPI.HasComponent<Enemy>(hit.Entity) || !chilled.Add(hit.Entity)) continue;
                EffectApply.AddChill(chilledLookup, hit.Entity, active.Field.ChillPerTick, settings.ChillMaxStacks, settings.ChillDuration);
            }
        }
    }

    struct ActiveField
    {
        public TimezoneField Field;
        public float3 Position;
    }
}

/// <summary>Placeholder art: each Timezone field as a sphere.</summary>
[UpdateInGroup(typeof(FeedbackSystemGroup))]
[BurstCompile]
partial struct TimezoneFieldUISystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var color = new Color(0.6f, 0.45f, 1f);
        foreach (var (field, transform) in SystemAPI.Query<RefRO<TimezoneField>, RefRO<LocalTransform>>())
            ProgrammerArt.DrawSphere(transform.ValueRO.Position, field.ValueRO.Radius, color);
    }
}
