using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Ball Lightning: flies the unit in a straight line for as long as the skill is held, the way it faced at take-off (the
/// cursor, or the controller's aim). It flies Lift above whatever is below, rising over walls and props, Invulnerable
/// and draining resource. Letting go, running out of resource, a wall too tall to rise over or the edge of the map ends
/// the flight on the closest floor back along the path, then releases a lightning attack scaled by the resource spent. While flying it
/// moves the unit itself (UnitMoverSystem skips it) and no other skill, moving or turning works (SkillActivationSystem,
/// CharacterMovementSystem).
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial struct BallLightningSystem : ISystem
{
    const float LookAhead = 2f;         // Meters ahead checked for walls to rise over in time
    const float MaxRise = 4f;           // Height of the tallest wall or prop it can rise over, above the floor below
    const float VerticalSpeed = 10f;    // Meters per second it rises or sinks
    const float EdgeDepth = 30f;        // Meters below the unit's bottom with nothing at all: the edge of the map
    const float BackOffStep = 0.25f;    // Meters per step when looking for a landing spot back along the path
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
        var hits = new NativeList<ColliderCastHit>(8, Allocator.Temp);
        state.CompleteDependency();  // The lookup is used here on the main thread; TimedEffectSystem ticks it in a job

        // Flying (the component is disabled meanwhile: SkillActivationSystem blocks every skill, so the hold is read here)
        foreach (var (ball, localTransform, collider, resource, resourceChanges, entity) in
                 SystemAPI.Query<RefRW<BallLightning>, RefRW<LocalTransform>, RefRO<PhysicsCollider>, RefRO<Resource>, DynamicBuffer<ResourceChanger>>()
                          .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            ref var b = ref ball.ValueRW;
            var stats = SystemAPI.GetComponent<CalculatedStats>(entity);
            if (!b.Flying)
            {
                b.Elapsed = math.min(b.Elapsed + deltaTime, SkillUtil.Cooldown(b.Cooldown, stats.Value));
                continue;
            }

            uint floorLayers = FloorLayers(ref state, entity);
            float drain = SkillUtil.Cost(b.ResourceCostPerSecond, stats.Value) * deltaTime;
            if (!IsHeld(ref state, entity) || resource.ValueRO.CurrentValue < drain)
            {
                LandBack(ref b, ref localTransform.ValueRW, world, collider.ValueRO, entity, floorLayers, minWalkableNormalY, commandBuffer, stats);
                continue;
            }
            resourceChanges.Add(new ResourceChanger{Value = -drain});
            b.Spent += drain;
            if (invulnerableLookup.HasComponent(entity)) EffectApply.Refresh(invulnerableLookup, entity, InvulnerableLinger);

            var position = localTransform.ValueRO.Position;
            float2 direction = b.Direction.xz;

            // The floor it flies over (the ground under a wall, not its top), where MaxRise is measured from; kept over
            // water, gaps and deep drops
            float bottom = UnitMovement.BottomOffset(collider.ValueRO, localTransform.ValueRO.Rotation, localTransform.ValueRO.Scale);
            if (UnitMovement.TryFindFloor(world, position, bottom, 0.5f, b.Lift + MaxRise + 2f, floorLayers, out float floorBelow))
                b.FloorY = floorBelow;

            // Height: Lift above the highest thing just ahead, so it rises over walls in time. The body is swept down
            // from MaxRise above that floor, so it sees what the sweep ahead below will; starting inside something means
            // it's taller
            float riseTop = b.FloorY + MaxRise;
            float targetY = position.y;
            bool found = false;
            float highest = float.MinValue;
            var drop = new float3(0f, -(2f * MaxRise + b.Lift + 2f), 0f);
            for (float ahead = 0f; ahead <= LookAhead; ahead += 1f)
            {
                var sample = new float3(position.x + direction.x * ahead, riseTop + bottom, position.z + direction.y * ahead);
                float fall = UnitMovement.Sweep(world, collider.ValueRO, sample, drop, localTransform.ValueRO.Rotation, localTransform.ValueRO.Scale,
                                                entity, floorLayers | UnitMovement.WorldLayers, minWalkableNormalY, ref hits, out _);
                if (fall <= 0f || fall >= 1f) continue;
                found = true;
                highest = math.max(highest, sample.y + drop.y * fall - bottom);
            }
            if (found) targetY = highest + bottom + b.Lift;
            position.y += math.clamp(targetY - position.y, -VerticalSpeed * deltaTime, VerticalSpeed * deltaTime);

            // The edge of the map (nothing at all below the next spot; water or a deep floor still count) ends the flight
            var move = new float3(direction.x, 0f, direction.y) * (b.Speed * deltaTime);
            if (!UnitMovement.TryFindFloor(world, position + move, bottom, MaxRise, EdgeDepth,
                                           floorLayers | (uint)CollisionLayerHelper.Water | UnitMovement.WorldLayers, out _))
            {
                localTransform.ValueRW.Position = position;
                LandBack(ref b, ref localTransform.ValueRW, world, collider.ValueRO, entity, floorLayers, minWalkableNormalY, commandBuffer, stats);
                continue;
            }

            // Ahead: up to a wall it's still rising over, and down when it's too tall to rise over
            float free = UnitMovement.Sweep(world, collider.ValueRO, position, move, localTransform.ValueRO.Rotation, localTransform.ValueRO.Scale,
                                            entity, UnitMovement.WorldLayers, minWalkableNormalY, ref hits, out _);
            localTransform.ValueRW.Position = position + move * free;
            if (free < 1f && targetY - position.y <= 0.01f)
                LandBack(ref b, ref localTransform.ValueRW, world, collider.ValueRO, entity, floorLayers, minWalkableNormalY, commandBuffer, stats);
        }

        // Starting a flight
        foreach (var (ball, localTransform, desiredDirection, resource, entity) in
                 SystemAPI.Query<RefRW<BallLightning>, RefRW<LocalTransform>, RefRO<DesiredDirection>, RefRO<Resource>>()
                          .WithEntityAccess())
        {
            ref var b = ref ball.ValueRW;
            var calculatedStats = SystemAPI.GetComponent<CalculatedStats>(entity);
            var stats = calculatedStats.Value;
            float cost = SkillUtil.Cost(b.ResourceCost, stats);
            if (b.Flying || b.Elapsed < SkillUtil.Cooldown(b.Cooldown, stats)) continue;
            // Turns toward the aim first and takes off once facing it; that direction is kept for the whole flight
            bool facing = SkillUtil.TurnToward(ref localTransform.ValueRW, desiredDirection.ValueRO, config, deltaTime);
            if (!facing || !SkillUtil.CanAfford(resource.ValueRO.CurrentValue, cost, calculatedStats)) continue;
            var direction = math.normalizesafe(new float3(desiredDirection.ValueRO.Direction.x, 0f, desiredDirection.ValueRO.Direction.z));
            if (math.lengthsq(direction) < 0.5f) continue;

            localTransform.ValueRW.Rotation = quaternion.LookRotationSafe(direction, math.up());
            b.Flying = true;
            b.Start = localTransform.ValueRO.Position;
            b.FloorY = b.Start.y - UnitMovement.BottomOffset(SystemAPI.GetComponent<PhysicsCollider>(entity), localTransform.ValueRO.Rotation, localTransform.ValueRO.Scale);
            b.Direction = direction;
            b.Spent = cost;
            SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger{Value = -cost});
            if (invulnerableLookup.HasComponent(entity)) EffectApply.Refresh(invulnerableLookup, entity, InvulnerableLinger);

            // A use (tagged Mobility, so it also raises MobilitySkillUsed for e.g. Refractionist)
            SkillUtil.MarkUsed(SystemAPI.GetBuffer<Skill>(entity), SkillType.BallLightning);
        }

        // Report cooldown and flying state (after SkillActivationSystem's defaults)
        foreach (var (ball, skillBuffer, stats) in SystemAPI.Query<RefRW<BallLightning>, DynamicBuffer<Skill>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            int index = -1;
            if (SkillUtil.FindSkill(skillBuffer, SkillType.BallLightning, ref index))
            {
                ref var skill = ref skillBuffer.ElementAt(index);
                SkillUtil.ApplyCooldownReduction(ref skill, ref ball.ValueRW.Elapsed);
                float cooldown = SkillUtil.Cooldown(ball.ValueRO.Cooldown, stats.ValueRO.Value);
                skill.State.CooldownDuration = cooldown;
                skill.State.CooldownRemaining = math.max(0f, cooldown - ball.ValueRO.Elapsed);
                skill.State.IsActive = ball.ValueRO.Flying;
            }
        }
    }

    // Down on the closest floor back toward where it took off (there if none), then the landing attack. Each spot back
    // looks straight down from the flight height, so it lands right under it when there's floor it fits on
    static void LandBack(ref BallLightning ball, ref LocalTransform transform, in CollisionWorld world, in PhysicsCollider collider, Entity entity, uint floorLayers,
                         float minWalkableNormalY, EntityCommandBuffer commandBuffer, in CalculatedStats stats)
    {
        var position = transform.Position;
        float bottom = UnitMovement.BottomOffset(collider, transform.Rotation, transform.Scale);
        float2 back = ball.Start.xz - position.xz;
        float length = math.length(back);
        float2 direction = length > 1e-3f ? back / length : float2.zero;
        var landing = ball.Start;
        for (float distance = 0f; distance <= length; distance += BackOffStep)
        {
            var spot = position;
            spot.xz += direction * distance;
            if (!UnitMovement.TryFindFloor(world, spot, bottom, 0.5f, EdgeDepth, floorLayers, out float floorY)) continue;
            spot.y = floorY + bottom + UnitMovement.Skin;
            if (!TeleportSystem.Fits(world, collider, spot, transform.Rotation, transform.Scale, entity, minWalkableNormalY)) continue;
            landing = spot;
            break;
        }
        Land(ref ball, ref transform, landing, commandBuffer, entity, stats);
    }

    // Ends the flight at landing and releases the lightning attack there (one frame, like a punch)
    static void Land(ref BallLightning ball, ref LocalTransform transform, float3 landing, EntityCommandBuffer commandBuffer, Entity caster, in CalculatedStats stats)
    {
        transform.Position = landing;
        ball.Flying = false;
        ball.Elapsed = 0f;

        float damage = SkillUtil.CooldownDamage(ball.Damage * (1f + ball.Spent / math.max(ball.SpentToDouble, 1e-3f)), ball.Cooldown, stats);
        var newEntity = commandBuffer.CreateEntity();
        commandBuffer.AddComponent(newEntity, new LocalTransform{Position = landing, Rotation = quaternion.identity, Scale = 1f});
        commandBuffer.AddComponent(newEntity, new Attack{Origin = DamageLabel.BallLightning, Damage = damage, Type = DamageType.Lightning, Radius = ball.Radius, DamageHierarchy = DamageHierarchy.Main, HitForce = ball.HitForce});
        commandBuffer.AddComponent(newEntity, new Source{Entity = caster});
        commandBuffer.AddComponent(newEntity, new AttackTarget{TargetType = TargetType.Enemy});
        commandBuffer.AddComponent(newEntity, new InitializeFromSource{});
        commandBuffer.AddComponent(newEntity, new DestroyWithChildren{});
    }

    // The slot Ball Lightning is in is held (a Mirror Image copy gets its owner's inputs)
    bool IsHeld(ref SystemState state, Entity entity)
    {
        if (!SystemAPI.HasComponent<InputsData>(entity) || !SystemAPI.HasBuffer<Skill>(entity)) return false;
        int index = -1;
        var skills = SystemAPI.GetBuffer<Skill>(entity);
        if (!SkillUtil.FindSkill(skills, SkillType.BallLightning, ref index)) return false;
        int slot = skills[index].SlotIndex;
        return slot >= 0 && slot < 4 && SystemAPI.GetComponent<InputsData>(entity).SkillHeld[slot];
    }

    // Water counts as floor for units that can walk on it
    uint FloorLayers(ref SystemState state, Entity entity)
    {
        bool waterWalking = SystemAPI.HasComponent<WaterWalking>(entity) && SystemAPI.IsComponentEnabled<WaterWalking>(entity);
        return UnitMovement.GroundLayer | (waterWalking ? (uint)CollisionLayerHelper.Water : 0u);
    }
}
