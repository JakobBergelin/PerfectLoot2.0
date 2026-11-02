using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Shadowstep: when off cooldown, grants the Shadowstep buff for Duration, scaled by BuffDuration (restarting it if it
/// was already on). No resource cost. The buff's attack speed is added by ShadowstepBuffStatsSystem, its jumps done by
/// ShadowstepJumpSystem, and it runs out through TimedEffectSystem.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial struct ShadowstepSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        var buffLookup = SystemAPI.GetComponentLookup<ShadowstepBuff>();
        state.CompleteDependency();  // The lookup is used here on the main thread; TimedEffectSystem ticks it in a job

        foreach (var (shadowstep, stats) in SystemAPI.Query<RefRW<Shadowstep>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
            shadowstep.ValueRW.Elapsed = math.min(shadowstep.ValueRO.Elapsed + deltaTime, SkillUtil.Cooldown(shadowstep.ValueRO.Cooldown, stats.ValueRO.Value));

        foreach (var (shadowstep, skillBuffer, stats, entity) in
                 SystemAPI.Query<RefRW<Shadowstep>, DynamicBuffer<Skill>, RefRO<CalculatedStats>>().WithEntityAccess())
        {
            if (shadowstep.ValueRO.Elapsed < SkillUtil.Cooldown(shadowstep.ValueRO.Cooldown, stats.ValueRO.Value)) continue;
            if (!buffLookup.HasComponent(entity)) continue;
            // BuffDuration of -100% or less: nothing to grant, so no cast
            float duration = EffectApply.BuffDuration(shadowstep.ValueRO.Duration, stats.ValueRO.Value);
            if (duration <= 0f) continue;

            shadowstep.ValueRW.Elapsed = 0f;
            var buff = EffectApply.Refresh(buffLookup, entity, duration);
            buff.ValueRW.AttackSpeedBonus = shadowstep.ValueRO.AttackSpeedBonus;
            buff.ValueRW.Range = shadowstep.ValueRO.Range;
            SkillUtil.MarkUsed(skillBuffer, SkillType.Shadowstep);
        }

        // Report cooldown, and active while the buff lasts (after SkillActivationSystem's defaults)
        foreach (var (shadowstep, skillBuffer, stats, entity) in SystemAPI.Query<RefRO<Shadowstep>, DynamicBuffer<Skill>, RefRO<CalculatedStats>>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            int index = -1;
            if (SkillUtil.FindSkill(skillBuffer, SkillType.Shadowstep, ref index))
            {
                ref var skill = ref skillBuffer.ElementAt(index);
                float cooldown = SkillUtil.Cooldown(shadowstep.ValueRO.Cooldown, stats.ValueRO.Value);
                skill.State.CooldownDuration = cooldown;
                skill.State.CooldownRemaining = math.max(0f, cooldown - shadowstep.ValueRO.Elapsed);
                skill.State.IsActive = buffLookup.HasComponent(entity) && buffLookup.IsComponentEnabled(entity);
            }
        }
    }
}

/// <summary>Adds the Shadowstep buff's attack speed while it is active.</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct ShadowstepBuffStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (buff, stats) in SystemAPI.Query<RefRO<ShadowstepBuff>, RefRW<CalculatedStats>>())
            stats.ValueRW.Value.AttackSpeed += buff.ValueRO.AttackSpeedBonus;
    }
}

/// <summary>
/// While the Shadowstep buff lasts, using a melee skill (pressed, with the resource for it) first jumps the unit in front
/// of the enemy nearest the cursor: within CursorPickRadius of the cursor and the buff's Range of the unit. It lands
/// WeaponReach from the enemy facing it, so the hit this frame lands on it. Only when that enemy is out of reach; like
/// Teleport, walls don't stop it, and it lands where it fits on floor (pulled back toward the unit when needed).
/// Before the melee skills, so their hits come from the new spot. Hookshot Slash (also Mobility) moves the unit itself
/// and doesn't jump. The jump isn't a Mobility use.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
[UpdateBefore(typeof(FistsOfThunderSystem))]
[UpdateBefore(typeof(WhirlwindSystem))]
public partial struct ShadowstepJumpSystem : ISystem
{
    const float CursorPickRadius = 3f;  // Meters around the cursor to look for the target
    const float ReachMargin = 0.5f;     // Meters beyond WeaponReach an enemy still counts as in reach
    const float MinJump = 0.5f;         // Meters; a landing closer than this to the unit isn't a jump

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<MovementConfig>();
        state.RequireForUpdate<ShadowstepBuff>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var world = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        var config = SystemAPI.GetSingleton<MovementConfig>();
        float minWalkableNormalY = math.cos(math.radians(config.MaxSlope));
        var enemyFilter = new CollisionFilter { BelongsTo = ~0u, CollidesWith = (uint)CollisionLayerHelper.AnyEnemy, GroupIndex = 0 };
        var hits = new NativeList<DistanceHit>(Allocator.Temp);

        foreach (var (buff, localTransform, desiredDirection, inputs, collider, stats, skills, entity) in
                 SystemAPI.Query<RefRO<ShadowstepBuff>, RefRW<LocalTransform>, RefRW<DesiredDirection>, RefRO<InputsData>, RefRO<PhysicsCollider>,
                                 RefRO<CalculatedStats>, DynamicBuffer<Skill>>().WithEntityAccess())
        {
            if (!UsingMeleeSkill(skills)) continue;

            var position = localTransform.ValueRO.Position;
            var cursor = inputs.ValueRO.MousePosition;
            if (!TryFindTarget(ref state, world, hits, enemyFilter, entity, position, cursor, buff.ValueRO.Range, out var targetPosition)) continue;

            float reach = stats.ValueRO.Value.WeaponReach;
            var toTarget = targetPosition.xz - position.xz;
            float distance = math.length(toTarget);
            if (distance <= reach + ReachMargin) continue;
            var direction = toTarget / distance;

            // In front of the enemy, on its side toward the unit
            var spot = new float3(targetPosition.x - direction.x * reach, targetPosition.y, targetPosition.z - direction.y * reach);
            bool waterWalking = SystemAPI.HasComponent<WaterWalking>(entity) && SystemAPI.IsComponentEnabled<WaterWalking>(entity);
            uint floorLayers = UnitMovement.GroundLayer | (waterWalking ? (uint)CollisionLayerHelper.Water : 0u);
            if (!TeleportSystem.TryFindLanding(world, collider.ValueRO, localTransform.ValueRO, entity, spot, distance,
                                               minWalkableNormalY, config.StepHeight, floorLayers, out var landing))
                continue;
            if (math.distance(landing.xz, position.xz) < MinJump) continue;

            var facing = new float3(direction.x, 0f, direction.y);
            var rotation = quaternion.LookRotationSafe(facing, math.up());
            localTransform.ValueRW.Position = landing;
            localTransform.ValueRW.Rotation = rotation;
            desiredDirection.ValueRW.Direction = facing;
            desiredDirection.ValueRW.Quaternion = rotation;
        }
    }

    // A pressed melee skill this frame that can be paid for (Hookshot Slash, also Mobility, moves the unit itself)
    static bool UsingMeleeSkill(DynamicBuffer<Skill> skills)
    {
        foreach (var skill in skills)
            if (skill.State.IsActive && skill.State.HasEnoughResource
                && (skill.Tags & SkillTags.Melee) != 0 && (skill.Tags & SkillTags.Mobility) == 0)
                return true;
        return false;
    }

    // The living enemy nearest the cursor, within CursorPickRadius of it and range of the unit
    bool TryFindTarget(ref SystemState state, in CollisionWorld world, NativeList<DistanceHit> hits, CollisionFilter filter, Entity self,
                       float3 position, float3 cursor, float range, out float3 targetPosition)
    {
        targetPosition = float3.zero;
        hits.Clear();
        world.OverlapSphere(cursor, CursorPickRadius, ref hits, filter);

        bool found = false;
        float best = float.MaxValue;
        foreach (var hit in hits)
        {
            if (hit.Entity == self || !SystemAPI.HasComponent<Health>(hit.Entity) || !SystemAPI.HasComponent<LocalTransform>(hit.Entity)) continue;
            if (SystemAPI.GetComponent<Health>(hit.Entity).CurrentValue <= 0f) continue;
            var enemyPosition = SystemAPI.GetComponent<LocalTransform>(hit.Entity).Position;
            if (math.distance(enemyPosition.xz, position.xz) > range) continue;
            float toCursor = math.distancesq(enemyPosition.xz, cursor.xz);
            if (toCursor >= best) continue;
            best = toCursor;
            targetPosition = enemyPosition;
            found = true;
        }
        return found;
    }
}
