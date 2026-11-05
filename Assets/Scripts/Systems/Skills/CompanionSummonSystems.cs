using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Every CompanionSkill: summons its companion in front of the caster (on the same spots Teleport can land on; with none
/// it isn't cast). One at a time: while it lives the skill stays on cooldown, which starts running when it dies. It goes
/// away if its owner dies or unequips the skill.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial struct CompanionSummonSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<CompanionSummon>();
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

        foreach (var summons in SystemAPI.Query<DynamicBuffer<CompanionSummon>>())
        {
            for (int i = 0; i < summons.Length; i++)
                summons.ElementAt(i).HasUnit = false;
        }

        foreach (var (unit, companion, entity) in SystemAPI.Query<RefRO<SummonedCompanion>, RefRO<Companion>>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            var owner = companion.ValueRO.Owner;
            int index = -1;
            if (SystemAPI.HasBuffer<CompanionSummon>(owner) && SystemAPI.GetComponent<Health>(owner).CurrentValue > 0f)
                index = CompanionSummon.Find(SystemAPI.GetBuffer<CompanionSummon>(owner), unit.ValueRO.Type);

            if (index >= 0) SystemAPI.GetBuffer<CompanionSummon>(owner).ElementAt(index).HasUnit = true;
            else commandBuffer.AddComponent<DestroyWithChildren>(entity);
        }

        foreach (var (summons, transform, collider, resource, skills, calculatedStats, entity) in
                 SystemAPI.Query<DynamicBuffer<CompanionSummon>, RefRO<LocalTransform>, RefRO<PhysicsCollider>, RefRO<Resource>, DynamicBuffer<Skill>, RefRO<CalculatedStats>>().WithEntityAccess())
        {
            var stats = calculatedStats.ValueRO.Value;
            for (int i = 0; i < summons.Length; i++)
            {
                ref var summon = ref summons.ElementAt(i);
                float cooldown = SkillUtil.Cooldown(summon.Cooldown, stats);

                float cost = SkillUtil.Cost(summon.ResourceCost, stats);
                if (summon.Held && !summon.HasUnit && summon.Elapsed >= cooldown && SkillUtil.CanAfford(resource.ValueRO.CurrentValue, cost, calculatedStats.ValueRO))
                {
                    var inFront = transform.ValueRO.Position + math.forward(transform.ValueRO.Rotation) * summon.SpawnDistance;
                    if (TeleportSystem.TryFindLanding(world, collider.ValueRO, transform.ValueRO, entity, inFront, summon.SpawnDistance,
                                                      minWalkableNormalY, config.StepHeight, UnitMovement.GroundLayer, out var spot))
                    {
                        var unit = commandBuffer.Instantiate(summon.Prefab);
                        commandBuffer.SetComponent(unit, new LocalTransform{Position = spot, Rotation = transform.ValueRO.Rotation, Scale = summon.Scale});
                        summon.HasUnit = true;
                        SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger{Value = -cost});
                        SkillUtil.MarkUsed(skills, summon.Type);
                    }
                }

                // Count the cooldown and report it (after SkillActivationSystem's defaults)
                summon.Elapsed = summon.HasUnit ? 0f : math.min(summon.Elapsed + deltaTime, cooldown);
                int index = -1;
                if (SkillUtil.FindSkill(skills, summon.Type, ref index))
                {
                    ref var skill = ref skills.ElementAt(index);
                    SkillUtil.ApplyCooldownReduction(ref skill, ref summon.Elapsed);
                    skill.State.CooldownDuration = cooldown;
                    skill.State.CooldownRemaining = math.max(0f, cooldown - summon.Elapsed);
                }
            }
        }
    }
}
