using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// EarthBending: when off cooldown and the caster can pay, raises a wall at the cursor (no further than Range, on the
/// same spots Teleport can land on; with none it isn't cast), standing on the ground and facing the caster. It replaces
/// the caster's old wall. Its health is HealthShare of the caster's max health; area of effect makes it bigger. A wall
/// goes away when it's broken (CheckDeathSystem), or when its owner dies or unequips the skill.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial struct EarthBendingSystem : ISystem
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

        foreach (var (wall, entity) in SystemAPI.Query<RefRO<EarthWall>>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            var owner = wall.ValueRO.Owner;
            bool ownerGone = !SystemAPI.HasComponent<EarthBending>(owner) || SystemAPI.GetComponent<Health>(owner).CurrentValue <= 0f;
            if (ownerGone) commandBuffer.AddComponent<DestroyWithChildren>(entity);
        }

        foreach (var (bending, transform, inputs, collider, resource, health, skills, entity) in
                 SystemAPI.Query<RefRW<EarthBending>, RefRO<LocalTransform>, RefRO<InputsData>, RefRO<PhysicsCollider>, RefRO<Resource>, RefRO<Health>, DynamicBuffer<Skill>>().WithEntityAccess())
        {
            var calculatedStats = SystemAPI.GetComponent<CalculatedStats>(entity);
            var stats = calculatedStats.Value;
            ref var b = ref bending.ValueRW;
            if (b.Elapsed < SkillUtil.Cooldown(b.Cooldown, stats)) continue;
            float cost = SkillUtil.Cost(b.ResourceCost, stats);
            if (!SkillUtil.CanAfford(resource.ValueRO.CurrentValue, cost, calculatedStats)) continue;
            if (!TeleportSystem.TryFindLanding(world, collider.ValueRO, transform.ValueRO, entity, inputs.ValueRO.MousePosition, b.Range,
                                               minWalkableNormalY, config.StepHeight, UnitMovement.GroundLayer, out var spot))
                continue;

            foreach (var (oldWall, oldEntity) in SystemAPI.Query<RefRO<EarthWall>>().WithNone<DestroyWithChildren>().WithEntityAccess())
                if (oldWall.ValueRO.Owner == entity) commandBuffer.AddComponent<DestroyWithChildren>(oldEntity);

            // On the ground under the spot (the spot is where the caster's own body would stand), its face toward the caster
            float floorY = spot.y - UnitMovement.BottomOffset(collider.ValueRO, transform.ValueRO.Rotation, transform.ValueRO.Scale);
            var towardCaster = transform.ValueRO.Position - spot;
            towardCaster.y = 0f;
            var rotation = quaternion.LookRotationSafe(math.normalizesafe(towardCaster, new float3(0f, 0f, -1f)), math.up());
            float wallHealth = math.max(1f, health.ValueRO.MaxValue * b.HealthShare);
            Spawn(commandBuffer, b.Prefab, new float3(spot.x, floorY, spot.z), rotation, math.max(0.1f, stats.AreaMultiplier), wallHealth);

            b.Elapsed = 0f;
            SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger { Value = -cost });
            SkillUtil.MarkUsed(skills, SkillType.EarthBending);
        }

        // Count the cooldown and report it (after SkillActivationSystem's defaults)
        foreach (var (bending, skillBuffer, stats) in SystemAPI.Query<RefRW<EarthBending>, DynamicBuffer<Skill>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            float cooldown = SkillUtil.Cooldown(bending.ValueRO.Cooldown, stats.ValueRO.Value);
            bending.ValueRW.Elapsed = math.min(bending.ValueRO.Elapsed + deltaTime, cooldown);

            int index = -1;
            if (!SkillUtil.FindSkill(skillBuffer, SkillType.EarthBending, ref index)) continue;
            ref var skill = ref skillBuffer.ElementAt(index);
            SkillUtil.ApplyCooldownReduction(ref skill, ref bending.ValueRW.Elapsed);
            skill.State.CooldownDuration = cooldown;
            skill.State.CooldownRemaining = math.max(0f, cooldown - bending.ValueRO.Elapsed);
        }
    }

    // A copy of the caster's wall prefab, its health set (its stats are just that health; FillHealthAndResource fills it)
    static void Spawn(EntityCommandBuffer commandBuffer, Entity prefab, float3 position, quaternion rotation, float scale, float health)
    {
        var newEntity = commandBuffer.Instantiate(prefab);
        commandBuffer.SetComponent(newEntity, new LocalTransform { Position = position, Rotation = rotation, Scale = scale });
        var stats = new StatBlock { Health = health };
        commandBuffer.SetComponent(newEntity, new BaseStats { Value = stats });
        commandBuffer.SetComponent(newEntity, new CalculatedStats { Value = stats });
        commandBuffer.SetComponent(newEntity, new Health { MaxValue = health, CurrentValue = health });
    }
}
