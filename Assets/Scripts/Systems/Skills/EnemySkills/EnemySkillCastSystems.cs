using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Decides when enemies and companions use their skills: counts down the cooldowns, then takes the first skill in the list that's ready
/// (cooldown, range, line of sight, resource, conditions), turns toward the target and starts it. Without a telegraph it
/// goes off at once; with one the enemy stands still until the marker is full. Being frozen cancels it and the cooldown still
/// counts. Going off adds an EnemySkillFire, which the skill's own system reads.
/// </summary>
[UpdateInGroup(typeof(SkillSystemGroup))]
[BurstCompile]
public partial struct EnemySkillCastSystem : ISystem
{
    const float MovingSpeedSq = 0.01f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EnemySkillCast>();
        state.RequireForUpdate<FlowFields>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var fields = SystemAPI.GetSingleton<FlowFields>();
        var physics = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        bool hasMarker = SystemAPI.TryGetSingleton<TelegraphPrefab>(out var markerPrefab);
        float deltaTime = SystemAPI.Time.DeltaTime;

        var targetables = SystemAPI.GetComponentLookup<EnemyTargetable>(true);
        var companions = SystemAPI.GetComponentLookup<Companion>(true);
        var healths = SystemAPI.GetComponentLookup<Health>(true);
        var motions = SystemAPI.GetComponentLookup<UnitMotion>(true);
        var skills = SystemAPI.GetBufferLookup<Skill>(true);
        state.CompleteDependency();  // The flow fields and lookups are used here on the main thread

        foreach (var (castRef, slots, fires, npc, transform, target, stats, entity) in
                 SystemAPI.Query<RefRW<EnemySkillCast>, DynamicBuffer<EnemySkillSlot>, DynamicBuffer<EnemySkillFire>, RefRW<NpcBehavior>,
                                 RefRW<LocalTransform>, RefRO<EnemyTarget>, RefRO<CalculatedStats>>().WithNone<SpawnRise>().WithEntityAccess())
        {
            fires.Clear();
            ref var cast = ref castRef.ValueRW;
            bool preventsActions = SystemAPI.HasComponent<MovementModifiers>(entity) && SystemAPI.GetComponent<MovementModifiers>(entity).PreventsActions;

            var unit = target.ValueRO.Unit;
            bool hasTarget = healths.HasComponent(unit) && healths[unit].CurrentValue > 0f;
            var unitPosition = hasTarget ? SystemAPI.GetComponent<LocalTransform>(unit).Position : float3.zero;
            float distance = math.distance(unitPosition.xz, transform.ValueRO.Position.xz);

            for (int i = 0; i < slots.Length; i++)
            {
                ref var slot = ref slots.ElementAt(i);
                bool inRange = hasTarget && !preventsActions && distance >= slot.MinRange && distance <= slot.MaxRange;
                if (slot.CooldownOnlyInRange && !inRange) slot.CooldownRemaining = slot.Cooldown;
                else slot.CooldownRemaining -= deltaTime * (slot.CooldownUsesAttackSpeed ? stats.ValueRO.Value.AttackSpeed : 1f);
            }

            if (cast.Active)
            {
                cast.TimeRemaining -= deltaTime;
                if (preventsActions || cast.TimeRemaining <= 0f)
                {
                    cast.Active = false;
                    npc.ValueRW.IsCasting = false;
                    if (!preventsActions) Fire(fires, ref npc.ValueRW, slots[cast.Slot].Type, cast.Position, cast.Direction);
                }
                continue;
            }
            // Sight is on the FlowField of the target's player; a companion's target has none, so its owner's
            var fieldUnit = companions.HasComponent(entity) ? companions[entity].Owner : unit;
            if (preventsActions || !hasTarget || !targetables.HasComponent(fieldUnit)) continue;

            var position = transform.ValueRO.Position;
            var field = fields[targetables[fieldUnit].FieldSlot];
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot.CooldownRemaining > 0f || distance < slot.MinRange || distance > slot.MaxRange) continue;
                if (slot.NeedsLineOfSight && !field.CanSee(position, unitPosition)) continue;
                if (!MeetsConditions(slot.Conditions, unit, healths, motions, skills)) continue;

                float cost = SkillUtil.Cost(slot.ResourceCost, stats.ValueRO.Value);
                if (cost > 0f && (!SystemAPI.HasComponent<Resource>(entity) || !SkillUtil.CanAfford(SystemAPI.GetComponent<Resource>(entity).CurrentValue, cost, stats.ValueRO))) continue;

                // The first skill it can use: it waits for this one rather than trying the next
                var direction = math.normalizesafe(new float3(unitPosition.x - position.x, 0f, unitPosition.z - position.z), math.forward(transform.ValueRO.Rotation));
                if (!TurnToward(ref transform.ValueRW, direction, slot.TurnSpeed, slot.FacingAngle, deltaTime)) break;

                slots.ElementAt(i).CooldownRemaining = slot.Cooldown;
                if (cost > 0f) SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger { Value = -cost });

                if (slot.Telegraph.Shape == TelegraphShape.None)
                {
                    Fire(fires, ref npc.ValueRW, slot.Type, unitPosition, direction);
                    break;
                }

                cast.Active = true;
                cast.Slot = i;
                cast.CastId++;
                cast.Duration = math.max(slot.Telegraph.Duration, 0.01f);
                cast.TimeRemaining = cast.Duration;
                cast.Position = unitPosition;
                cast.Direction = direction;
                npc.ValueRW.IsCasting = true;
                if (hasMarker)
                {
                    var origin = slot.Telegraph.Shape == TelegraphShape.Circle && !slot.Telegraph.OnSelf ? unitPosition : position;
                    TelegraphUtil.Spawn(commandBuffer, markerPrefab.Value, slot.Telegraph, entity, cast.CastId, TelegraphUtil.OnGround(physics, origin), direction);
                }
                break;
            }
        }
    }

    static void Fire(DynamicBuffer<EnemySkillFire> fires, ref NpcBehavior npc, EnemySkillType type, float3 position, float3 direction)
    {
        fires.Add(new EnemySkillFire { Type = type, Position = position, Direction = direction });
        npc.AttackCount++;
    }

    static bool MeetsConditions(in EnemySkillConditions conditions, Entity unit, in ComponentLookup<Health> healths, in ComponentLookup<UnitMotion> motions, in BufferLookup<Skill> skills)
    {
        if (conditions.TargetUsingSkill && !IsUsingSkill(unit, conditions.SkillTags, skills)) return false;

        if (conditions.TargetMotion != TargetMotion.Any && motions.HasComponent(unit))
        {
            bool moving = math.lengthsq(motions[unit].Actual.xz) > MovingSpeedSq;
            if (moving != (conditions.TargetMotion == TargetMotion.Moving)) return false;
        }

        var health = healths[unit];
        return conditions.TargetHealthBelow <= 0f || health.CurrentValue <= health.MaxValue * conditions.TargetHealthBelow;
    }

    // A unit without skills (a companion, say) is never using one
    static bool IsUsingSkill(Entity unit, SkillTags tags, in BufferLookup<Skill> skills)
    {
        if (!skills.HasBuffer(unit)) return false;
        var unitSkills = skills[unit];
        for (int i = 0; i < unitSkills.Length; i++)
        {
            var skill = unitSkills[i];
            if (skill.State.IsActive && (tags == 0 || (skill.Tags & tags) != 0)) return true;
        }
        return false;
    }

    /// <summary>Turns toward direction at turnSpeed degrees per second; whether it now faces within facingAngle degrees.</summary>
    static bool TurnToward(ref LocalTransform transform, float3 direction, float turnSpeed, float facingAngle, float deltaTime)
    {
        var wanted = quaternion.LookRotationSafe(direction, math.up());
        float angle = math.degrees(2f * math.acos(math.min(math.abs(math.dot(transform.Rotation.value, wanted.value)), 1f)));
        float step = turnSpeed * deltaTime;
        if (angle > 0f) transform.Rotation = math.slerp(transform.Rotation, wanted, math.saturate(step / angle));
        return angle - step <= facingAngle;
    }
}
