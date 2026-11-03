using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Hookshot Slash: pressing it (with the resource to pay) turns the unit toward the cursor and fires the hook. The hook
/// flies flat to the cursor spot, at most Range away, over enemies and water; a wall on the way latches it early. Once
/// latched the unit is pulled to it, ending before walls and on floor (DashSystem.DashEnd), with a HookshotSlashAttack
/// travelling along. Meanwhile it moves the unit itself (UnitMoverSystem and CharacterMovementSystem skip it) and no other
/// skill works (SkillActivationSystem). The latch is a Mobility use; each enemy hit is a Melee use (HookshotSlashAttackSystem).
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial struct HookshotSlashSystem : ISystem
{
    const float MinHookDistance = 1f;   // Meters; a cursor on the unit still throws the hook this far
    const float ArriveDistance = 0.05f;

    Unity.Mathematics.Random _random;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _random = Unity.Mathematics.Random.CreateFromIndex(0x48534C53u);
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
        var wallFilter = new CollisionFilter { BelongsTo = ~0u, CollidesWith = UnitMovement.WorldLayers };

        foreach (var (hookshot, localTransform, inputs, desiredDirection, collider, resource, stats, entity) in
                 SystemAPI.Query<RefRW<HookshotSlash>, RefRW<LocalTransform>, RefRO<InputsData>, RefRO<DesiredDirection>, RefRO<PhysicsCollider>, RefRO<Resource>, RefRO<CalculatedStats>>()
                          .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            ref var h = ref hookshot.ValueRW;
            var position = localTransform.ValueRO.Position;
            // Water counts as floor for units that can walk on it
            bool waterWalking = SystemAPI.HasComponent<WaterWalking>(entity) && SystemAPI.IsComponentEnabled<WaterWalking>(entity);
            uint floorLayers = UnitMovement.GroundLayer | (waterWalking ? (uint)CollisionLayerHelper.Water : 0u);

            if (h.State == HookshotState.Idle)
            {
                float cost = SkillUtil.Cost(h.ResourceCost, stats.ValueRO.Value);
                if (!SystemAPI.IsComponentEnabled<HookshotSlash>(entity)) continue;
                // Turns toward the cursor first and fires once facing it
                bool facing = SkillUtil.TurnToward(ref localTransform.ValueRW, desiredDirection.ValueRO, config, deltaTime);
                if (!facing || !SkillUtil.CanAfford(resource.ValueRO.CurrentValue, cost, stats.ValueRO)) continue;

                // Flat toward the cursor; the way the unit faces when the cursor is on it
                var toCursor = inputs.ValueRO.MousePosition - position;
                toCursor.y = 0f;
                float cursorDistance = math.length(toCursor);
                var forward = math.mul(localTransform.ValueRO.Rotation, new float3(0f, 0f, 1f));
                var fallback = math.normalizesafe(new float3(forward.x, 0f, forward.z), new float3(0f, 0f, 1f));
                h.Direction = cursorDistance > 1e-3f ? toCursor / cursorDistance : fallback;
                h.HookDistance = math.clamp(cursorDistance, MinHookDistance, math.max(h.Range, MinHookDistance));
                h.HookPosition = position;
                h.State = HookshotState.Firing;
                localTransform.ValueRW.Rotation = quaternion.LookRotationSafe(h.Direction, math.up());
                SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger{Value = -cost});
                continue;
            }

            if (h.State == HookshotState.Firing)
            {
                float travelled = math.distance(h.HookPosition.xz, position.xz);
                float step = math.min(h.HookSpeed * deltaTime, math.max(0f, h.HookDistance - travelled));
                var next = h.HookPosition + h.Direction * step;
                bool latched = travelled + step >= h.HookDistance - 1e-3f;

                // A wall on the way latches it there
                if (world.CastRay(new RaycastInput { Start = h.HookPosition, End = next, Filter = wallFilter }, out var hit))
                {
                    next = hit.Position;
                    latched = true;
                }
                h.HookPosition = next;
                if (!latched) continue;

                // Latched: pulled as far as the hook, before walls and on floor
                h.PullEnd = DashSystem.DashEnd(world, collider.ValueRO, localTransform.ValueRO, entity, h.Direction, math.distance(h.HookPosition.xz, position.xz),
                                               minWalkableNormalY, config.StepHeight, floorLayers);
                h.State = HookshotState.Pulling;
                h.HitsSeen = 0;
                SpawnAttack(commandBuffer, entity, h, stats.ValueRO.Value, position, ref _random);

                // The launch is a Mobility use (e.g. Refractionist); its hits are the Melee uses
                if (SystemAPI.HasBuffer<Skill>(entity)) SkillUtil.MarkUsed(SystemAPI.GetBuffer<Skill>(entity), SkillType.HookshotSlash, usedAs: SkillTags.Mobility);
                continue;
            }

            // Pulling: straight to the end, on the floor where there is some (over water it keeps its height)
            var offset = h.PullEnd.xz - position.xz;
            float remaining = math.length(offset);
            float pullStep = h.PullSpeed * deltaTime;
            if (remaining <= math.max(pullStep, ArriveDistance))
            {
                localTransform.ValueRW.Position = h.PullEnd;
                h.State = HookshotState.Idle;
                continue;
            }
            position.xz += offset / remaining * pullStep;
            float bottom = UnitMovement.BottomOffset(collider.ValueRO, localTransform.ValueRO.Rotation, localTransform.ValueRO.Scale);
            if (UnitMovement.TryFindFloor(world, position, bottom, config.StepHeight, config.StepHeight + 0.5f, floorLayers, out float floorY))
                position.y = floorY + bottom + UnitMovement.Skin;
            localTransform.ValueRW.Position = position;
        }

        // Report the active state (no cooldown; after SkillActivationSystem's defaults)
        foreach (var (hookshot, skillBuffer) in SystemAPI.Query<RefRO<HookshotSlash>, DynamicBuffer<Skill>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            int index = -1;
            if (SkillUtil.FindSkill(skillBuffer, SkillType.HookshotSlash, ref index))
                skillBuffer.ElementAt(index).State.IsActive = hookshot.ValueRO.State != HookshotState.Idle;
        }
    }

    /// <summary>The slash travelling with the pull: WeaponDamage (rolled) * WeaponDamageMultiplier, once per enemy (long grace period).</summary>
    static void SpawnAttack(EntityCommandBuffer commandBuffer, Entity caster, in HookshotSlash hookshot, in StatBlock stats, float3 position, ref Unity.Mathematics.Random random)
    {
        float weaponDamage = stats.WeaponDamage + random.NextFloat(-stats.WeaponDamageRange, stats.WeaponDamageRange);
        var newEntity = commandBuffer.CreateEntity();
        commandBuffer.AddComponent(newEntity, new LocalTransform{Position = position, Rotation = quaternion.identity, Scale = 1f});
        commandBuffer.AddComponent(newEntity, new Attack{Damage = math.max(0f, weaponDamage * hookshot.WeaponDamageMultiplier), Type = DamageType.Physical,
            DamageHierarchy = DamageHierarchy.Main, Radius = hookshot.Radius, HitForce = hookshot.HitForce, GracePeriod = 2f});
        commandBuffer.AddComponent(newEntity, new Source{Entity = caster});
        commandBuffer.AddComponent(newEntity, new AttackTarget{TargetType = TargetType.Enemy});
        commandBuffer.AddComponent(newEntity, new InitializeFromSource{});
        commandBuffer.AddComponent(newEntity, new HookshotSlashAttack{});
    }
}

/// <summary>
/// Keeps each Hookshot Slash attack on its pulled unit and removes it once the pull has ended (or the skill is gone).
/// Every enemy it has newly hit (its IgnoreEntity entries) makes this frame a Melee use of the skill (e.g. Echoing Blade).
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
[UpdateAfter(typeof(HookshotSlashSystem))]
public partial struct HookshotSlashAttackSystem : ISystem
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
        foreach (var (source, entity) in SystemAPI.Query<RefRO<Source>>().WithAll<HookshotSlashAttack, LocalTransform>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            var owner = source.ValueRO.Entity;
            bool hasSkill = SystemAPI.HasComponent<HookshotSlash>(owner);

            // Hits since last frame, counted even on the frame the pull ends
            if (hasSkill && SystemAPI.HasBuffer<IgnoreEntity>(entity))
            {
                int hits = SystemAPI.GetBuffer<IgnoreEntity>(entity).Length;
                var hookshot = SystemAPI.GetComponentRW<HookshotSlash>(owner);
                if (hits > hookshot.ValueRO.HitsSeen)
                {
                    hookshot.ValueRW.HitsSeen = hits;
                    if (SystemAPI.HasBuffer<Skill>(owner)) SkillUtil.MarkUsed(SystemAPI.GetBuffer<Skill>(owner), SkillType.HookshotSlash, usedAs: SkillTags.Melee);
                }
            }

            if (!hasSkill || SystemAPI.GetComponent<HookshotSlash>(owner).State != HookshotState.Pulling || !SystemAPI.HasComponent<LocalTransform>(owner))
            {
                commandBuffer.AddComponent<DestroyWithChildren>(entity);
                continue;
            }
            SystemAPI.GetComponentRW<LocalTransform>(entity).ValueRW.Position = SystemAPI.GetComponent<LocalTransform>(owner).Position;
        }
    }
}

/// <summary>Draws the hook while it's out: a sphere at the hook and a chain of dots back to the unit (placeholder art).</summary>
[BurstCompile]
[UpdateInGroup(typeof(FeedbackSystemGroup))]
partial struct HookshotSlashUISystem : ISystem
{
    const float ChainSpacing = 0.6f;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (hookshot, localTransform) in SystemAPI.Query<RefRO<HookshotSlash>, RefRO<LocalTransform>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (hookshot.ValueRO.State == HookshotState.Idle) continue;
            var hook = hookshot.ValueRO.HookPosition;
            var unit = localTransform.ValueRO.Position;
            ProgrammerArt.DrawSphere(hook, 0.2f, Color.gray);
            float length = math.distance(hook, unit);
            for (float along = ChainSpacing; along < length; along += ChainSpacing)
                ProgrammerArt.DrawSphere(math.lerp(unit, hook, along / length), 0.05f, Color.gray, 4);
        }
    }
}
