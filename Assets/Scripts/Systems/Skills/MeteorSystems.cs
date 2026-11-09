using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Meteor: once the caster faces the cursor, has recovered and can pay, it calls a meteor down on the cursor, clamped to
/// Range from the caster (a MeteorStrike there). Repeats while held, AttackSpeed apart (the shared AttackRecovery).
/// </summary>
[UpdateInGroup(typeof(SkillSystemGroup))]
[BurstCompile]
public partial struct MeteorSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Meteor>();
        state.RequireForUpdate<MovementConfig>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // SkillState: no cooldown to report (the time between casts is the shared AttackRecovery); IsActive/resource set by SkillActivationSystem
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var movementConfig = SystemAPI.GetSingleton<MovementConfig>();
        float deltaTime = SystemAPI.Time.DeltaTime;

        var resourceChanges = SystemAPI.GetBufferLookup<ResourceChanger>();
        var skills = SystemAPI.GetBufferLookup<Skill>();
        foreach (var (meteor, localTransform, desiredDirection, inputs, resource, stats, recovery, entity) in
                 SystemAPI.Query<RefRO<Meteor>, RefRW<LocalTransform>, RefRO<DesiredDirection>, RefRO<InputsData>, RefRO<Resource>, RefRO<CalculatedStats>,
                     RefRW<AttackRecovery>>().WithEntityAccess())
        {
            bool facing = SkillUtil.TurnToward(ref localTransform.ValueRW, desiredDirection.ValueRO, movementConfig, deltaTime);
            if (recovery.ValueRO.Remaining > 0f) continue;

            var m = meteor.ValueRO;
            float cost = SkillUtil.Cost(m.ResourceCost, stats.ValueRO.Value);
            if (!SkillUtil.CanAfford(resource.ValueRO.CurrentValue, cost, stats.ValueRO) || !facing) continue;

            // The cursor, at most Range away on the ground plane
            var origin = localTransform.ValueRO.Position;
            var target = inputs.ValueRO.MousePosition;
            var offset = new float2(target.x - origin.x, target.z - origin.z);
            float distance = math.length(offset);
            if (distance > m.Range) offset *= m.Range / distance;
            var spot = new float3(origin.x + offset.x, target.y, origin.z + offset.y);

            var strike = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(strike, LocalTransform.FromPosition(spot));
            commandBuffer.AddComponent(strike, new MeteorStrike
            {
                Delay = m.Delay, FallTime = m.Delay, Damage = m.Damage, Radius = m.Radius,
                HazardDamagePerSecond = m.HazardDamagePerSecond, HazardRadius = m.HazardRadius, HazardDuration = m.HazardDuration,
            });
            commandBuffer.AddComponent(strike, new Source { Entity = entity });

            recovery.ValueRW.Remaining += m.AttackSpeed;
            if (resourceChanges.HasBuffer(entity)) resourceChanges[entity].Add(new ResourceChanger { Value = -cost });
            if (skills.HasBuffer(entity)) SkillUtil.MarkUsed(skills[entity], SkillType.Meteor);
        }
    }
}

/// <summary>
/// Falling meteors: counts each one's Delay down; at the impact it becomes a one-frame fire attack where it is (its
/// source's stats apply then, InitializeFromSource) and leaves a fire Hazard there, scaled by the source's damage %,
/// conversion and area of effect at that moment. A meteor whose source is gone just disappears.
/// </summary>
[UpdateInGroup(typeof(SkillSystemGroup))]
[BurstCompile]
public partial struct MeteorStrikeSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<MeteorStrike>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var sourceStats = SystemAPI.GetComponentLookup<CalculatedStats>(true);

        foreach (var (strike, transform, source, entity) in SystemAPI.Query<RefRW<MeteorStrike>, RefRO<LocalTransform>, RefRO<Source>>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            strike.ValueRW.Delay -= deltaTime;
            var m = strike.ValueRO;
            if (m.Delay > 0f) continue;

            commandBuffer.RemoveComponent<MeteorStrike>(entity);
            commandBuffer.AddComponent(entity, new DestroyWithChildren());   // A single-frame hit
            if (!sourceStats.HasComponent(source.ValueRO.Entity)) continue;

            // The impact
            commandBuffer.AddComponent(entity, new Attack { Origin = DamageLabel.Meteor, Damage = m.Damage, Type = DamageType.Fire, Radius = m.Radius, DamageHierarchy = DamageHierarchy.Main });
            commandBuffer.AddComponent(entity, new AttackTarget { TargetType = TargetType.Enemy });
            commandBuffer.AddComponent(entity, new InitializeFromSource());

            // The burning ground it leaves
            var s = sourceStats[source.ValueRO.Entity].Value;
            var type = s.ConvertedType(DamageType.Fire);
            var hazard = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(hazard, LocalTransform.FromPosition(transform.ValueRO.Position));
            commandBuffer.AddComponent(hazard, new Hazard
            {
                Type = type, DamagePerSecond = m.HazardDamagePerSecond * s.DamageMultiplier(type), Radius = m.HazardRadius * s.AreaMultiplier,
                TimeRemaining = m.HazardDuration, Origin = DamageLabel.Meteor,
            });
            commandBuffer.AddComponent(hazard, source.ValueRO);
        }
    }
}

/// <summary>Placeholder art: where each falling meteor will land, a circle of its impact radius that closes in as it falls.</summary>
[UpdateInGroup(typeof(FeedbackSystemGroup))]
[BurstCompile]
partial struct MeteorStrikeUISystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var sourceStats = SystemAPI.GetComponentLookup<CalculatedStats>(true);
        foreach (var (strike, transform, source) in SystemAPI.Query<RefRO<MeteorStrike>, RefRO<LocalTransform>, RefRO<Source>>())
        {
            float area = sourceStats.HasComponent(source.ValueRO.Entity) ? sourceStats[source.ValueRO.Entity].Value.AreaMultiplier : 1f;
            float radius = strike.ValueRO.Radius * area;
            var center = transform.ValueRO.Position;
            DrawCircle(center, radius, Color.orangeRed);
            DrawCircle(center, radius * math.saturate(strike.ValueRO.Delay / math.max(strike.ValueRO.FallTime, 1e-3f)), Color.yellow);
        }
    }

    static void DrawCircle(float3 center, float radius, Color color)
    {
        const int Segments = 32;
        for (int i = 0; i < Segments; i++)
        {
            float a = i * math.PI2 / Segments, b = (i + 1) * math.PI2 / Segments;
            Debug.DrawLine(center + new float3(math.cos(a), 0.05f, math.sin(a)) * new float3(radius, 1f, radius),
                           center + new float3(math.cos(b), 0.05f, math.sin(b)) * new float3(radius, 1f, radius), color);
        }
    }
}
