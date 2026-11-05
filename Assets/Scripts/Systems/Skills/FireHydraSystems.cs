using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Fire Hydra: places a Hydra at the cursor (no further than Range, on the same spots Teleport can land on; with none it
/// isn't cast), replacing the caster's old one. It stands there for Duration, shooting with its EnemySkills, and goes
/// away early if its owner dies or unequips the skill.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial struct FireHydraSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<MovementConfig>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var world = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        var config = SystemAPI.GetSingleton<MovementConfig>();
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        float minWalkableNormalY = math.cos(math.radians(config.MaxSlope));
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (unit, companion, entity) in SystemAPI.Query<RefRW<FireHydraUnit>, RefRO<Companion>>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            unit.ValueRW.TimeRemaining -= deltaTime;
            var owner = companion.ValueRO.Owner;
            bool ownerGone = !SystemAPI.HasComponent<FireHydra>(owner) || SystemAPI.GetComponent<Health>(owner).CurrentValue <= 0f;
            if (ownerGone || unit.ValueRO.TimeRemaining <= 0f) commandBuffer.AddComponent<DestroyWithChildren>(entity);
        }

        foreach (var (hydra, transform, inputs, collider, resource, skills, entity) in
                 SystemAPI.Query<RefRW<FireHydra>, RefRO<LocalTransform>, RefRO<InputsData>, RefRO<PhysicsCollider>, RefRO<Resource>, DynamicBuffer<Skill>>().WithEntityAccess())
        {
            var calculatedStats = SystemAPI.GetComponent<CalculatedStats>(entity);
            var stats = calculatedStats.Value;
            if (hydra.ValueRO.Elapsed < SkillUtil.Cooldown(hydra.ValueRO.Cooldown, stats)) continue;
            float cost = SkillUtil.Cost(hydra.ValueRO.ResourceCost, stats);
            if (!SkillUtil.CanAfford(resource.ValueRO.CurrentValue, cost, calculatedStats)) continue;
            if (!TeleportSystem.TryFindLanding(world, collider.ValueRO, transform.ValueRO, entity, inputs.ValueRO.MousePosition, hydra.ValueRO.Range,
                                               minWalkableNormalY, config.StepHeight, UnitMovement.GroundLayer, out var spot))
                continue;

            foreach (var (companion, oldHydra) in SystemAPI.Query<RefRO<Companion>>().WithAll<FireHydraUnit>().WithNone<DestroyWithChildren>().WithEntityAccess())
            {
                if (companion.ValueRO.Owner == entity) commandBuffer.AddComponent<DestroyWithChildren>(oldHydra);
            }

            Spawn(commandBuffer, hydra.ValueRO, spot);
            hydra.ValueRW.Elapsed = 0f;
            SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger{Value = -cost});
            SkillUtil.MarkUsed(skills, SkillType.FireHydra);
        }

        // Count the cooldown and report it (after SkillActivationSystem's defaults)
        foreach (var (hydra, skillBuffer, stats) in SystemAPI.Query<RefRW<FireHydra>, DynamicBuffer<Skill>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            float cooldown = SkillUtil.Cooldown(hydra.ValueRO.Cooldown, stats.ValueRO.Value);
            hydra.ValueRW.Elapsed = math.min(hydra.ValueRO.Elapsed + deltaTime, cooldown);

            int index = -1;
            if (SkillUtil.FindSkill(skillBuffer, SkillType.FireHydra, ref index))
            {
                ref var skill = ref skillBuffer.ElementAt(index);
                SkillUtil.ApplyCooldownReduction(ref skill, ref hydra.ValueRW.Elapsed);
                skill.State.CooldownDuration = cooldown;
                skill.State.CooldownRemaining = math.max(0f, cooldown - hydra.ValueRO.Elapsed);
            }
        }
    }

    static void Spawn(EntityCommandBuffer commandBuffer, in FireHydra hydra, float3 position)
    {
        var newEntity = commandBuffer.Instantiate(hydra.Prefab);
        commandBuffer.SetComponent(newEntity, new LocalTransform{Position = position, Rotation = quaternion.identity, Scale = 1f});
        commandBuffer.SetComponent(newEntity, new FireHydraUnit{TimeRemaining = hydra.Duration});
    }
}

/// <summary>Placeholder art: each Hydra as a sphere.</summary>
[UpdateInGroup(typeof(FeedbackSystemGroup))]
[BurstCompile]
partial struct FireHydraUISystem : ISystem
{
    const float Radius = 0.6f;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var transform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<FireHydraUnit>())
            ProgrammerArt.DrawSphere(transform.ValueRO.Position, Radius, Color.orangeRed);
    }
}
