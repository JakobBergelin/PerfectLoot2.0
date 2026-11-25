using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Charge: runs at the living enemy nearest the cursor (within CursorPickRadius of it and Range of the unit), in a straight
/// line at Speed, up to the weapon's reach from it. Like Dash it passes through enemies and over water but not through
/// walls; an enemy it can't reach that way can't be charged (nothing happens, no cooldown spent). On arrival it gives
/// ResourceGain and the Charge buff. While charging it moves the unit itself (UnitMoverSystem and CharacterMovementSystem
/// skip it).
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial struct ChargeSystem : ISystem
{
    const float CursorPickRadius = 3f;  // Meters around the cursor to look for the target
    const float BodyMargin = 0.6f;      // Meters it keeps from the target's body when that is further than the reach
    const float BlockedMargin = 0.5f;   // Meters short of the spot that count as a blocked way

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<MovementConfig>();
        state.RequireForUpdate<Charge>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var world = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        var config = SystemAPI.GetSingleton<MovementConfig>();
        float minWalkableNormalY = math.cos(math.radians(config.MaxSlope));
        float deltaTime = SystemAPI.Time.DeltaTime;
        var buffLookup = SystemAPI.GetComponentLookup<ChargeBuff>();
        state.CompleteDependency();  // The lookup is used here on the main thread; TimedEffectSystem ticks it in a job
        var enemyFilter = new CollisionFilter { BelongsTo = ~0u, CollidesWith = (uint)CollisionLayerHelper.AnyEnemy, GroupIndex = 0 };
        var hits = new NativeList<DistanceHit>(Allocator.Temp);

        // Charging, and the cooldown
        foreach (var (charge, localTransform, collider, stats, entity) in
                 SystemAPI.Query<RefRW<Charge>, RefRW<LocalTransform>, RefRO<PhysicsCollider>, RefRO<CalculatedStats>>()
                          .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            ref var c = ref charge.ValueRW;
            c.Elapsed = math.min(c.Elapsed + deltaTime, SkillUtil.Cooldown(c.Cooldown, stats.ValueRO.Value));
            if (!c.Charging) continue;

            var position = localTransform.ValueRO.Position;
            var offset = c.NextPosition.xz - position.xz;
            float remaining = math.length(offset);
            float step = c.Speed * deltaTime;
            if (remaining > step)
            {
                // On the floor where there is some; over water or a gap it keeps its height
                position.xz += offset / remaining * step;
                float bottom = UnitMovement.BottomOffset(collider.ValueRO, localTransform.ValueRO.Rotation, localTransform.ValueRO.Scale);
                if (UnitMovement.TryFindFloor(world, position, bottom, config.StepHeight, config.StepHeight + 0.5f, FloorLayers(ref state, entity), out float floorY))
                    position.y = floorY + bottom + UnitMovement.Skin;
                localTransform.ValueRW.Position = position;
                continue;
            }

            localTransform.ValueRW.Position = c.NextPosition;
            c.Charging = false;
            SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger { Value = c.ResourceGain });
            float duration = EffectApply.BuffDuration(c.BuffDuration, stats.ValueRO.Value);
            if (duration <= 0f || !buffLookup.HasComponent(entity)) continue;
            var buff = EffectApply.Refresh(buffLookup, entity, duration);
            buff.ValueRW.AttackSpeedBonus = c.AttackSpeedBonus;
            buff.ValueRW.AttacksLeft = c.BuffAttacks;
        }

        foreach (var (charge, localTransform, desiredDirection, inputs, collider, stats, skillBuffer, entity) in
                 SystemAPI.Query<RefRW<Charge>, RefRW<LocalTransform>, RefRW<DesiredDirection>, RefRO<InputsData>, RefRO<PhysicsCollider>, RefRO<CalculatedStats>, DynamicBuffer<Skill>>()
                          .WithEntityAccess())
        {
            ref var c = ref charge.ValueRW;
            if (c.Charging || c.Elapsed < SkillUtil.Cooldown(c.Cooldown, stats.ValueRO.Value)) continue;
            var position = localTransform.ValueRO.Position;
            if (!TryFindTarget(ref state, world, hits, enemyFilter, position, inputs.ValueRO.MousePosition, c.Range, out var targetPosition, out float targetRadius)) continue;

            var toTarget = targetPosition.xz - position.xz;
            float distance = math.length(toTarget);
            var direction = distance > 1e-3f ? new float3(toTarget.x, 0f, toTarget.y) / distance : desiredDirection.ValueRO.Direction;
            float way = math.max(0f, distance - math.max(stats.ValueRO.Value.WeaponReach, targetRadius + BodyMargin));
            var end = DashSystem.DashEnd(world, collider.ValueRO, localTransform.ValueRO, entity, direction, way, minWalkableNormalY, config.StepHeight, FloorLayers(ref state, entity));
            if (math.distance(end.xz, position.xz) < way - BlockedMargin) continue;

            c.Elapsed = 0f;
            c.Charging = true;
            c.NextPosition = end;
            var rotation = quaternion.LookRotationSafe(direction, math.up());
            localTransform.ValueRW.Rotation = rotation;
            desiredDirection.ValueRW.Direction = direction;
            desiredDirection.ValueRW.Quaternion = rotation;
            SkillUtil.MarkUsed(skillBuffer, SkillType.Charge);
        }

        // Report cooldown and charging state (after SkillActivationSystem's defaults)
        foreach (var (charge, skillBuffer, stats) in SystemAPI.Query<RefRW<Charge>, DynamicBuffer<Skill>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            int index = -1;
            if (SkillUtil.FindSkill(skillBuffer, SkillType.Charge, ref index))
            {
                ref var skill = ref skillBuffer.ElementAt(index);
                SkillUtil.ApplyCooldownReduction(ref skill, ref charge.ValueRW.Elapsed);
                float cooldown = SkillUtil.Cooldown(charge.ValueRO.Cooldown, stats.ValueRO.Value);
                skill.State.CooldownDuration = cooldown;
                skill.State.CooldownRemaining = math.max(0f, cooldown - charge.ValueRO.Elapsed);
                skill.State.IsActive = charge.ValueRO.Charging;
            }
        }
    }

    // Water counts as floor for units that can walk on it
    uint FloorLayers(ref SystemState state, Entity entity)
    {
        bool waterWalking = SystemAPI.HasComponent<WaterWalking>(entity) && SystemAPI.IsComponentEnabled<WaterWalking>(entity);
        return UnitMovement.GroundLayer | (waterWalking ? (uint)CollisionLayerHelper.Water : 0u);
    }

    // The living enemy nearest the cursor, within CursorPickRadius of it and range of the unit, and how far its body reaches
    bool TryFindTarget(ref SystemState state, in CollisionWorld world, NativeList<DistanceHit> hits, CollisionFilter filter,
                       float3 position, float3 cursor, float range, out float3 targetPosition, out float targetRadius)
    {
        targetPosition = float3.zero;
        targetRadius = 0f;
        hits.Clear();
        world.OverlapSphere(cursor, CursorPickRadius, ref hits, filter);

        bool found = false;
        float best = float.MaxValue;
        foreach (var hit in hits)
        {
            if (!SystemAPI.HasComponent<Health>(hit.Entity) || !SystemAPI.HasComponent<LocalTransform>(hit.Entity)) continue;
            if (SystemAPI.GetComponent<Health>(hit.Entity).CurrentValue <= 0f) continue;
            var enemyPosition = SystemAPI.GetComponent<LocalTransform>(hit.Entity).Position;
            if (math.distance(enemyPosition.xz, position.xz) > range) continue;
            float toCursor = math.distancesq(enemyPosition.xz, cursor.xz);
            if (toCursor >= best) continue;
            best = toCursor;
            targetPosition = enemyPosition;
            targetRadius = SystemAPI.HasComponent<Steering>(hit.Entity) ? SystemAPI.GetComponent<Steering>(hit.Entity).Radius : 0f;
            found = true;
        }
        return found;
    }
}

/// <summary>Adds the Charge buff's attack speed while it is active.</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct ChargeBuffStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (buff, stats) in SystemAPI.Query<RefRO<ChargeBuff>, RefRW<CalculatedStats>>())
            stats.ValueRW.Value.AttackSpeed += buff.ValueRO.AttackSpeedBonus;
    }
}

/// <summary>
/// Charge buff: each attack made (a use of a Melee or Projectile skill, whatever it hits; not one an effect cast) uses one of
/// its attacks up, and the buff ends with the last one.
/// </summary>
[UpdateInGroup(typeof(SkillReactionSystemGroup))]
[BurstCompile]
public partial struct ChargeBuffSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (buff, active, skills) in SystemAPI.Query<RefRW<ChargeBuff>, EnabledRefRW<ChargeBuff>, DynamicBuffer<Skill>>())
        {
            foreach (var skill in skills)
                if (skill.events.OnUsed && !skill.events.AutoCast && (skill.Tags & skill.events.UsedAs & (SkillTags.Melee | SkillTags.Projectile)) != 0)
                    buff.ValueRW.AttacksLeft--;
            if (buff.ValueRO.AttacksLeft <= 0) active.ValueRW = false;
        }
    }
}
