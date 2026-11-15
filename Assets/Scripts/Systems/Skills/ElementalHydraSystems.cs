using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Elemental Hydra: places a Hydra at the cursor (no further than Range, on the same spots Teleport can land on; with none it
/// isn't cast), replacing the caster's old one. It stands there for Duration, shooting with its EnemySkills (or its owner's Elemental Projectile skills, see ElementalHydraTakeShotSystem), and goes
/// away early if its owner dies or unequips the skill.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial struct ElementalHydraSystem : ISystem
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

        foreach (var (unit, companion, entity) in SystemAPI.Query<RefRW<ElementalHydraUnit>, RefRO<Companion>>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            unit.ValueRW.TimeRemaining -= deltaTime;
            var owner = companion.ValueRO.Owner;
            bool ownerGone = !SystemAPI.HasComponent<ElementalHydra>(owner) || SystemAPI.GetComponent<Health>(owner).CurrentValue <= 0f;
            if (ownerGone || unit.ValueRO.TimeRemaining <= 0f) commandBuffer.AddComponent<DestroyWithChildren>(entity);
        }

        foreach (var (hydra, transform, inputs, collider, resource, skills, entity) in
                 SystemAPI.Query<RefRW<ElementalHydra>, RefRO<LocalTransform>, RefRO<InputsData>, RefRO<PhysicsCollider>, RefRO<Resource>, DynamicBuffer<Skill>>().WithEntityAccess())
        {
            var calculatedStats = SystemAPI.GetComponent<CalculatedStats>(entity);
            var stats = calculatedStats.Value;
            if (hydra.ValueRO.Elapsed < SkillUtil.Cooldown(hydra.ValueRO.Cooldown, stats)) continue;
            float cost = SkillUtil.Cost(hydra.ValueRO.ResourceCost, stats);
            if (!SkillUtil.CanAfford(resource.ValueRO.CurrentValue, cost, calculatedStats)) continue;
            if (!TeleportSystem.TryFindLanding(world, collider.ValueRO, transform.ValueRO, entity, inputs.ValueRO.MousePosition, hydra.ValueRO.Range,
                                               minWalkableNormalY, config.StepHeight, UnitMovement.GroundLayer, out var spot))
                continue;

            foreach (var (companion, oldHydra) in SystemAPI.Query<RefRO<Companion>>().WithAll<ElementalHydraUnit>().WithNone<DestroyWithChildren>().WithEntityAccess())
            {
                if (companion.ValueRO.Owner == entity) commandBuffer.AddComponent<DestroyWithChildren>(oldHydra);
            }

            Spawn(commandBuffer, hydra.ValueRO, spot);
            hydra.ValueRW.Elapsed = 0f;
            SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger{Value = -cost});
            SkillUtil.MarkUsed(skills, SkillType.ElementalHydra);
        }

        // Count the cooldown and report it (after SkillActivationSystem's defaults)
        foreach (var (hydra, skillBuffer, stats) in SystemAPI.Query<RefRW<ElementalHydra>, DynamicBuffer<Skill>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            float cooldown = SkillUtil.Cooldown(hydra.ValueRO.Cooldown, stats.ValueRO.Value);
            hydra.ValueRW.Elapsed = math.min(hydra.ValueRO.Elapsed + deltaTime, cooldown);

            int index = -1;
            if (SkillUtil.FindSkill(skillBuffer, SkillType.ElementalHydra, ref index))
            {
                ref var skill = ref skillBuffer.ElementAt(index);
                SkillUtil.ApplyCooldownReduction(ref skill, ref hydra.ValueRW.Elapsed);
                skill.State.CooldownDuration = cooldown;
                skill.State.CooldownRemaining = math.max(0f, cooldown - hydra.ValueRO.Elapsed);
            }
        }
    }

    static void Spawn(EntityCommandBuffer commandBuffer, in ElementalHydra hydra, float3 position)
    {
        var newEntity = commandBuffer.Instantiate(hydra.Prefab);
        commandBuffer.SetComponent(newEntity, new LocalTransform{Position = position, Rotation = quaternion.identity, Scale = 1f});
        commandBuffer.SetComponent(newEntity, new ElementalHydraUnit{TimeRemaining = hydra.Duration});
    }
}

/// <summary>
/// Elemental Hydra's shots, part 1: when a Hydra's own skill goes off and its owner has an equipped skill tagged both
/// Elemental and Projectile, the Hydra's own shot is taken away (before EnemyProjectileSystem fires it) and kept for
/// ElementalHydraShotSystem. With none, the Hydra fires its own skill as usual. Its timing and aim stay its own.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
[UpdateAfter(typeof(EnemySkillCastSystem))]
[UpdateBefore(typeof(EnemyProjectileSystem))]
public partial struct ElementalHydraTakeShotSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<ElementalHydraUnit>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (unit, companion, fires) in SystemAPI.Query<RefRW<ElementalHydraUnit>, RefRO<Companion>, DynamicBuffer<EnemySkillFire>>())
        {
            var owner = companion.ValueRO.Owner;
            if (fires.Length == 0 || !SystemAPI.HasBuffer<Skill>(owner) || !ElementalHydraShotSystem.HasShot(SystemAPI.GetBuffer<Skill>(owner))) continue;

            for (int i = fires.Length - 1; i >= 0; i--)
            {
                if (fires[i].Type != EnemySkillType.Projectile) continue;
                unit.ValueRW.ShotPending = true;
                unit.ValueRW.ShotDirection = fires[i].Direction;
                fires.RemoveAt(i);
            }
        }
    }
}

/// <summary>
/// Elemental Hydra's shots, part 2: each kept shot becomes a cast of one of the owner's Elemental Projectile skills, at
/// random, from the Hydra toward its target (SkillCastRequest with an Origin): free, with the owner's stats, counted as an
/// AutoCast use. After the skills' request cleanup, so the skill's own system casts it next frame.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillReactionSystemGroup))]
public partial struct ElementalHydraShotSystem : ISystem
{
    const SkillTags ShotTags = SkillTags.Elemental | SkillTags.Projectile;
    Unity.Mathematics.Random _random;

    public void OnCreate(ref SystemState state)
    {
        _random = Unity.Mathematics.Random.CreateFromIndex((uint)System.Environment.TickCount);
        state.RequireForUpdate<ElementalHydraUnit>();
    }

    /// <summary>Whether a skill list has one a Hydra can fire.</summary>
    public static bool HasShot(DynamicBuffer<Skill> skills)
    {
        foreach (var skill in skills)
            if ((skill.Tags & ShotTags) == ShotTags) return true;
        return false;
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (unit, companion, transform) in SystemAPI.Query<RefRW<ElementalHydraUnit>, RefRO<Companion>, RefRO<LocalTransform>>())
        {
            if (!unit.ValueRO.ShotPending) continue;
            unit.ValueRW.ShotPending = false;

            var owner = companion.ValueRO.Owner;
            if (!SystemAPI.HasBuffer<Skill>(owner) || !SystemAPI.HasBuffer<SkillCastRequest>(owner)) continue;
            var candidates = new FixedList64Bytes<SkillType>();
            foreach (var skill in SystemAPI.GetBuffer<Skill>(owner))
                if ((skill.Tags & ShotTags) == ShotTags && candidates.Length < candidates.Capacity) candidates.Add(skill.Type);
            if (candidates.Length == 0) continue;

            SystemAPI.GetBuffer<SkillCastRequest>(owner).Add(new SkillCastRequest
            {
                Type = candidates[_random.NextInt(candidates.Length)],
                Direction = unit.ValueRO.ShotDirection,
                AutoCast = true,
                Free = true,
                HasOrigin = true,
                Origin = transform.ValueRO.Position,
            });
        }
    }
}
