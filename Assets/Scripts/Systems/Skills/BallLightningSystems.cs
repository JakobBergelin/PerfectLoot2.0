using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Ball Lightning: flies the unit in a straight line to the cursor, picked like Teleport's landing (floor where its
/// collider fits, pulled back toward the unit). It flies Lift above whatever is below, rising over walls and props,
/// Invulnerable and draining resource. It lands at the spot, or when the drain can't be paid on the closest floor back
/// along the path, then releases a lightning attack scaled by the resource spent. While flying it moves the unit itself
/// (UnitMoverSystem skips it) and no other skill, moving or turning works (SkillActivationSystem, CharacterMovementSystem).
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial struct BallLightningSystem : ISystem
{
    const float LookAhead = 2f;         // Meters ahead checked for walls to rise over in time
    const float MaxRise = 4f;           // Meters above the unit's bottom it looks for something to fly over
    const float VerticalSpeed = 10f;    // Meters per second it rises or sinks
    const float DescendDistance = 1.5f; // Meters before the landing spot it starts sinking onto it
    const float InvulnerableLinger = 0.1f;  // The buff is refreshed every frame of the flight and runs out this long after

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
        var invulnerableLookup = SystemAPI.GetComponentLookup<Invulnerable>();
        state.CompleteDependency();  // The lookup is used here on the main thread; TimedEffectSystem ticks it in a job

        // Flying (the component is disabled meanwhile: SkillActivationSystem blocks every skill)
        foreach (var (ball, localTransform, collider, resource, resourceChanges, entity) in
                 SystemAPI.Query<RefRW<BallLightning>, RefRW<LocalTransform>, RefRO<PhysicsCollider>, RefRO<Resource>, DynamicBuffer<ResourceChanger>>()
                          .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            ref var b = ref ball.ValueRW;
            if (!b.Flying)
            {
                b.Elapsed = math.min(b.Elapsed + deltaTime, SkillUtil.Cooldown(b.Cooldown, SystemAPI.GetComponent<CalculatedStats>(entity).Value));
                continue;
            }

            var position = localTransform.ValueRO.Position;
            float drain = SkillUtil.Cost(b.ResourceCostPerSecond, SystemAPI.GetComponent<CalculatedStats>(entity).Value) * deltaTime;
            if (resource.ValueRO.CurrentValue < drain)
            {
                // Out of resource: down on the closest floor back toward where it started
                var from = localTransform.ValueRO;
                from.Position = b.Start;
                var landing = TeleportSystem.TryFindLanding(world, collider.ValueRO, from, entity, position, float.MaxValue, minWalkableNormalY,
                                                            config.StepHeight, FloorLayers(ref state, entity), out var spot) ? spot : b.Start;
                Land(ref b, ref localTransform.ValueRW, landing, commandBuffer, entity);
                continue;
            }
            resourceChanges.Add(new ResourceChanger{Value = -drain});
            b.Spent += drain;
            if (invulnerableLookup.HasComponent(entity)) EffectApply.Refresh(invulnerableLookup, entity, InvulnerableLinger);

            var offset = b.Destination.xz - position.xz;
            float remaining = math.length(offset);
            float step = b.Speed * deltaTime;
            if (remaining <= step)
            {
                Land(ref b, ref localTransform.ValueRW, b.Destination, commandBuffer, entity);
                continue;
            }

            var direction = offset / remaining;
            position.xz += direction * step;

            // Height: Lift above the highest thing just ahead (so it rises over walls in time), sinking onto the landing spot at the end
            float bottom = UnitMovement.BottomOffset(collider.ValueRO, localTransform.ValueRO.Rotation, localTransform.ValueRO.Scale);
            float targetY = position.y;
            if (remaining - step <= DescendDistance) targetY = b.Destination.y;
            else
            {
                bool found = false;
                float highest = float.MinValue;
                for (float ahead = 0f; ahead <= LookAhead; ahead += 1f)
                {
                    var sample = position;
                    sample.xz += direction * ahead;
                    if (UnitMovement.TryFindFloor(world, sample, bottom + b.Lift, MaxRise, MaxRise + b.Lift + 2f, FloorLayers(ref state, entity) | UnitMovement.WorldLayers, out float surfaceY))
                    {
                        found = true;
                        highest = math.max(highest, surfaceY);
                    }
                }
                if (found) targetY = highest + bottom + b.Lift;
            }
            position.y += math.clamp(targetY - position.y, -VerticalSpeed * deltaTime, VerticalSpeed * deltaTime);
            localTransform.ValueRW.Position = position;
        }

        // Starting a flight
        foreach (var (ball, localTransform, inputs, collider, resource, entity) in
                 SystemAPI.Query<RefRW<BallLightning>, RefRW<LocalTransform>, RefRO<InputsData>, RefRO<PhysicsCollider>, RefRO<Resource>>()
                          .WithEntityAccess())
        {
            ref var b = ref ball.ValueRW;
            var stats = SystemAPI.GetComponent<CalculatedStats>(entity).Value;
            float cost = SkillUtil.Cost(b.ResourceCost, stats);
            if (b.Flying || b.Elapsed < SkillUtil.Cooldown(b.Cooldown, stats) || resource.ValueRO.CurrentValue < cost) continue;
            if (!TeleportSystem.TryFindLanding(world, collider.ValueRO, localTransform.ValueRO, entity, inputs.ValueRO.MousePosition, float.MaxValue,
                                               minWalkableNormalY, config.StepHeight, FloorLayers(ref state, entity), out var destination))
                continue;

            var toDestination = destination - localTransform.ValueRO.Position;
            localTransform.ValueRW.Rotation = quaternion.LookRotationSafe(math.normalizesafe(new float3(toDestination.x, 0f, toDestination.z)), math.up());
            b.Flying = true;
            b.Start = localTransform.ValueRO.Position;
            b.Destination = destination;
            b.Spent = cost;
            SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger{Value = -cost});
            if (invulnerableLookup.HasComponent(entity)) EffectApply.Refresh(invulnerableLookup, entity, InvulnerableLinger);

            // A use (tagged Mobility, so it also raises MobilitySkillUsed for e.g. Refractionist)
            SkillUtil.MarkUsed(SystemAPI.GetBuffer<Skill>(entity), SkillType.BallLightning);
        }

        // Report cooldown and flying state (after SkillActivationSystem's defaults)
        foreach (var (ball, skillBuffer, stats) in SystemAPI.Query<RefRO<BallLightning>, DynamicBuffer<Skill>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            int index = -1;
            if (SkillUtil.FindSkill(skillBuffer, SkillType.BallLightning, ref index))
            {
                ref var skill = ref skillBuffer.ElementAt(index);
                float cooldown = SkillUtil.Cooldown(ball.ValueRO.Cooldown, stats.ValueRO.Value);
                skill.State.CooldownDuration = cooldown;
                skill.State.CooldownRemaining = math.max(0f, cooldown - ball.ValueRO.Elapsed);
                skill.State.IsActive = ball.ValueRO.Flying;
            }
        }
    }

    // Ends the flight at landing and releases the lightning attack there (one frame, like a punch)
    static void Land(ref BallLightning ball, ref LocalTransform transform, float3 landing, EntityCommandBuffer commandBuffer, Entity caster)
    {
        transform.Position = landing;
        ball.Flying = false;
        ball.Elapsed = 0f;

        float damage = ball.Damage * (1f + ball.Spent / math.max(ball.SpentToDouble, 1e-3f));
        var newEntity = commandBuffer.CreateEntity();
        commandBuffer.AddComponent(newEntity, new LocalTransform{Position = landing, Rotation = quaternion.identity, Scale = 1f});
        commandBuffer.AddComponent(newEntity, new Attack{Damage = damage, Type = DamageType.Lightning, Radius = ball.Radius, DamageHierarchy = DamageHierarchy.Main, HitForce = ball.HitForce});
        commandBuffer.AddComponent(newEntity, new Source{Entity = caster});
        commandBuffer.AddComponent(newEntity, new AttackTarget{TargetType = TargetType.Enemy});
        commandBuffer.AddComponent(newEntity, new InitializeFromSource{});
        commandBuffer.AddComponent(newEntity, new DestroyWithChildren{});
    }

    // Water counts as floor for units that can walk on it
    uint FloorLayers(ref SystemState state, Entity entity)
    {
        bool waterWalking = SystemAPI.HasComponent<WaterWalking>(entity) && SystemAPI.IsComponentEnabled<WaterWalking>(entity);
        return UnitMovement.GroundLayer | (waterWalking ? (uint)CollisionLayerHelper.Water : 0u);
    }
}
