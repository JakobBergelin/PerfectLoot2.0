using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Stone Guardian: summons a Guardian in front of the caster (on the same spots Teleport can land on; with none it isn't
/// cast). One at a time: while it lives the skill stays on cooldown, which starts running when it dies. It goes away if
/// its owner dies or unequips the skill.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial struct StoneGuardianSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<StoneGuardian>();
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

        var guarded = new NativeHashSet<Entity>(8, Allocator.Temp);  // Casters with a living Guardian
        foreach (var (companion, entity) in SystemAPI.Query<RefRO<Companion>>().WithAll<StoneGuardianUnit>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            var owner = companion.ValueRO.Owner;
            bool ownerGone = !SystemAPI.HasComponent<StoneGuardian>(owner) || SystemAPI.GetComponent<Health>(owner).CurrentValue <= 0f;
            if (ownerGone) commandBuffer.AddComponent<DestroyWithChildren>(entity);
            else guarded.Add(owner);
        }

        foreach (var (guardian, transform, collider, resource, skills, entity) in
                 SystemAPI.Query<RefRW<StoneGuardian>, RefRO<LocalTransform>, RefRO<PhysicsCollider>, RefRO<Resource>, DynamicBuffer<Skill>>().WithEntityAccess())
        {
            var calculatedStats = SystemAPI.GetComponent<CalculatedStats>(entity);
            var stats = calculatedStats.Value;
            if (guarded.Contains(entity) || guardian.ValueRO.Elapsed < SkillUtil.Cooldown(guardian.ValueRO.Cooldown, stats)) continue;
            float cost = SkillUtil.Cost(guardian.ValueRO.ResourceCost, stats);
            if (!SkillUtil.CanAfford(resource.ValueRO.CurrentValue, cost, calculatedStats)) continue;

            float distance = guardian.ValueRO.SpawnDistance;
            var inFront = transform.ValueRO.Position + math.forward(transform.ValueRO.Rotation) * distance;
            if (!TeleportSystem.TryFindLanding(world, collider.ValueRO, transform.ValueRO, entity, inFront, distance,
                                               minWalkableNormalY, config.StepHeight, UnitMovement.GroundLayer, out var spot))
                continue;

            var unit = commandBuffer.Instantiate(guardian.ValueRO.Prefab);
            commandBuffer.SetComponent(unit, new LocalTransform{Position = spot, Rotation = transform.ValueRO.Rotation, Scale = guardian.ValueRO.Scale});
            guardian.ValueRW.Elapsed = 0f;
            SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger{Value = -cost});
            SkillUtil.MarkUsed(skills, SkillType.StoneGuardian);
        }

        // Count the cooldown and report it (after SkillActivationSystem's defaults)
        foreach (var (guardian, skillBuffer, stats, entity) in SystemAPI.Query<RefRW<StoneGuardian>, DynamicBuffer<Skill>, RefRO<CalculatedStats>>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            float cooldown = SkillUtil.Cooldown(guardian.ValueRO.Cooldown, stats.ValueRO.Value);
            guardian.ValueRW.Elapsed = guarded.Contains(entity) ? 0f : math.min(guardian.ValueRO.Elapsed + deltaTime, cooldown);

            int index = -1;
            if (SkillUtil.FindSkill(skillBuffer, SkillType.StoneGuardian, ref index))
            {
                ref var skill = ref skillBuffer.ElementAt(index);
                SkillUtil.ApplyCooldownReduction(ref skill, ref guardian.ValueRW.Elapsed);
                skill.State.CooldownDuration = cooldown;
                skill.State.CooldownRemaining = math.max(0f, cooldown - guardian.ValueRO.Elapsed);
            }
        }
    }
}
