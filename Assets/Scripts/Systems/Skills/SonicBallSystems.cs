using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Sonic Ball while held: rolls the unit along its heading at its movement speed, following the floor. Left/right input
/// turns the heading (relative to the ball). Walls and water reflect it off their surface; an edge without floor (an empty
/// cell, the map edge) turns it back. Enemies don't stop it: the SonicBallAttack following it hits them, sized from the
/// unit's Armor when the roll starts. While rolling it moves the unit itself (UnitMoverSystem and CharacterMovementSystem
/// skip it) and no other skill works (SkillActivationSystem). Releasing it ends the roll and starts the cooldown.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial struct SonicBallSystem : ISystem
{
    const int MaxBounces = 3;   // Per frame, so a corner can't loop forever

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
        var hits = new NativeList<ColliderCastHit>(8, Allocator.Temp);

        foreach (var (ball, localTransform, inputs, collider, movementSpeed, stats, entity) in
                 SystemAPI.Query<RefRW<SonicBall>, RefRW<LocalTransform>, RefRO<InputsData>, RefRO<PhysicsCollider>, RefRO<MovementSpeed>, RefRO<CalculatedStats>>()
                          .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            ref var b = ref ball.ValueRW;
            bool held = SystemAPI.IsComponentEnabled<SonicBall>(entity);
            float cooldown = SkillUtil.Cooldown(b.Cooldown, stats.ValueRO.Value);

            if (!b.Rolling)
            {
                b.Elapsed = math.min(b.Elapsed + deltaTime, cooldown);
                if (!held || b.Elapsed < cooldown) continue;

                // Starts the way the unit faces
                var forward = math.mul(localTransform.ValueRO.Rotation, new float3(0f, 0f, 1f));
                b.Heading = math.normalizesafe(new float3(forward.x, 0f, forward.z), new float3(0f, 0f, 1f));
                b.Rolling = true;
                SpawnAttack(commandBuffer, entity, b, stats.ValueRO, localTransform.ValueRO.Position);

                // A use (tagged Mobility, so it also raises MobilitySkillUsed for e.g. Refractionist)
                if (SystemAPI.HasBuffer<Skill>(entity)) SkillUtil.MarkUsed(SystemAPI.GetBuffer<Skill>(entity), SkillType.SonicBall);
            }
            else if (!held)
            {
                // Released (or blocked, e.g. frozen): the cooldown starts; SonicBallAttackSystem removes the attack
                b.Rolling = false;
                b.Elapsed = 0f;
                continue;
            }

            // Steering: right input turns the ball to its own right (positive angle around up), left to its left
            float turn = inputs.ValueRO.Move.x * b.TurnSpeed * deltaTime;
            b.Heading = math.normalizesafe(math.rotate(quaternion.RotateY(turn), b.Heading), b.Heading);

            var modifiers = SystemAPI.HasComponent<MovementModifiers>(entity) ? SystemAPI.GetComponent<MovementModifiers>(entity) : new MovementModifiers { SpeedMultiplier = 1f };
            float remaining = config.MovementSpeed * math.max(0f, 1f + movementSpeed.ValueRO.Value) * modifiers.SpeedMultiplier * deltaTime;

            bool waterWalking = SystemAPI.HasComponent<WaterWalking>(entity) && SystemAPI.IsComponentEnabled<WaterWalking>(entity);
            uint floorLayers = UnitMovement.GroundLayer | (waterWalking ? (uint)CollisionLayerHelper.Water : 0u);
            uint blockLayers = UnitMovement.WorldLayers | (waterWalking ? 0u : (uint)CollisionLayerHelper.Water);

            var transform = localTransform.ValueRO;
            float bottom = UnitMovement.BottomOffset(collider.ValueRO, transform.Rotation, transform.Scale);
            var position = transform.Position;
            for (int i = 0; i < MaxBounces && remaining > 1e-4f; i++)
            {
                float fraction = UnitMovement.Sweep(world, collider.ValueRO, position, b.Heading * remaining, transform.Rotation, transform.Scale, entity,
                                                    blockLayers, minWalkableNormalY, ref hits, out var normal);
                float free = math.max(0f, remaining * fraction - (fraction < 1f ? UnitMovement.Skin : 0f));
                var next = position + b.Heading * free;

                // No floor there (an empty cell, the map edge): roll back the way it came
                if (!UnitMovement.TryFindFloor(world, next, bottom, config.StepHeight, config.StepHeight + 0.5f, floorLayers, out float floorY))
                {
                    b.Heading = -b.Heading;
                    break;
                }
                next.y = floorY + bottom + UnitMovement.Skin;
                position = next;
                if (fraction >= 1f) break;

                // Blocked by a wall or water: reflect off its surface and roll on with what's left of this frame's move
                remaining -= free;
                var flatNormal = math.normalizesafe(new float3(normal.x, 0f, normal.z));
                b.Heading = math.lengthsq(flatNormal) > 0f ? math.normalizesafe(math.reflect(b.Heading, flatNormal), -b.Heading) : -b.Heading;
            }

            localTransform.ValueRW.Position = position;
            localTransform.ValueRW.Rotation = quaternion.LookRotationSafe(b.Heading, math.up());
        }

        // Report cooldown and rolling state (after SkillActivationSystem's defaults)
        foreach (var (ball, skillBuffer, stats) in SystemAPI.Query<RefRW<SonicBall>, DynamicBuffer<Skill>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            int index = -1;
            if (SkillUtil.FindSkill(skillBuffer, SkillType.SonicBall, ref index))
            {
                ref var skill = ref skillBuffer.ElementAt(index);
                SkillUtil.ApplyCooldownReduction(ref skill, ref ball.ValueRW.Elapsed);
                float cooldown = SkillUtil.Cooldown(ball.ValueRO.Cooldown, stats.ValueRO.Value);
                skill.State.CooldownDuration = cooldown;
                skill.State.CooldownRemaining = ball.ValueRO.Rolling ? 0f : math.max(0f, cooldown - ball.ValueRO.Elapsed);
                skill.State.IsActive = ball.ValueRO.Rolling;
            }
        }
    }

    /// <summary>The attack rolling with the ball: (Armor + ArmorBonus) * ArmorDamageMultiplier per hit, the bonus not being in this frame's stats yet.</summary>
    static void SpawnAttack(EntityCommandBuffer commandBuffer, Entity caster, in SonicBall ball, in CalculatedStats stats, float3 position)
    {
        float damage = SkillUtil.CooldownDamage((stats.Value.Armor + ball.ArmorBonus) * ball.ArmorDamageMultiplier, ball.Cooldown, stats);
        var newEntity = commandBuffer.CreateEntity();
        commandBuffer.AddComponent(newEntity, new LocalTransform{Position = position, Rotation = quaternion.identity, Scale = 1f});
        commandBuffer.AddComponent(newEntity, new Attack{Damage = math.max(0f, damage), Type = DamageType.Physical, DamageHierarchy = DamageHierarchy.Main, Radius = ball.Radius, HitForce = ball.HitForce});
        commandBuffer.AddComponent(newEntity, new Source{Entity = caster});
        commandBuffer.AddComponent(newEntity, new AttackTarget{TargetType = TargetType.Enemy});
        commandBuffer.AddComponent(newEntity, new InitializeFromSource{});
        commandBuffer.AddComponent(newEntity, new SonicBallAttack{});
    }
}

/// <summary>Keeps each Sonic Ball attack on its rolling unit, and removes it once the roll has ended (or the skill is gone).</summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
[UpdateAfter(typeof(SonicBallSystem))]
public partial struct SonicBallAttackSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        foreach (var (source, entity) in SystemAPI.Query<RefRO<Source>>().WithAll<SonicBallAttack, LocalTransform>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            var owner = source.ValueRO.Entity;
            if (!SystemAPI.HasComponent<SonicBall>(owner) || !SystemAPI.GetComponent<SonicBall>(owner).Rolling || !SystemAPI.HasComponent<LocalTransform>(owner))
            {
                commandBuffer.AddComponent<DestroyWithChildren>(entity);
                continue;
            }
            SystemAPI.GetComponentRW<LocalTransform>(entity).ValueRW.Position = SystemAPI.GetComponent<LocalTransform>(owner).Position;
        }
    }
}

/// <summary>While rolling, the unit gets Sonic Ball's movement speed, Armor and Fire Resistance bonuses.</summary>
[BurstCompile]
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
public partial struct SonicBallStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (ball, stats) in SystemAPI.Query<RefRO<SonicBall>, RefRW<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (!ball.ValueRO.Rolling) continue;
            stats.ValueRW.Value.MovementSpeed += ball.ValueRO.MovementSpeedBonus;
            stats.ValueRW.Value.Armor += ball.ValueRO.ArmorBonus;
            stats.ValueRW.Value.FireResistance += ball.ValueRO.FireResistanceBonus;
        }
    }
}
