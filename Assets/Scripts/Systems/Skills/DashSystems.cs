using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Dash: a quick move toward the cursor. It passes through enemies and over water or empty cells, but stops at walls
/// and always ends on floor: the path is swept against the world when the dash starts and pulled back to the last spot
/// with floor. While dashing it moves the unit itself (UnitMoverSystem skips it), following the floor where there is any.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial struct DashSystem : ISystem
{
    const float Distance = 7f;
    const float MoveRate = 25f;         // Fraction of the remaining way per second (eased)
    const float BackOffStep = 0.25f;    // Meters per step when pulling the end back onto floor

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<MovementConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var world = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        var config = SystemAPI.GetSingleton<MovementConfig>();
        float minWalkableNormalY = math.cos(math.radians(config.MaxSlope));
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (dash, localTransform, collider, entity) in SystemAPI.Query<RefRW<Dash>, RefRW<LocalTransform>, RefRO<PhysicsCollider>>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            dash.ValueRW.Elapsed += deltaTime;
            dash.ValueRW.Elapsed = Mathf.Min(dash.ValueRW.Elapsed, SkillUtil.Cooldown(dash.ValueRO.Cooldown, SystemAPI.GetComponent<CalculatedStats>(entity).Value));

            if(dash.ValueRO.Dashing)
            {
                // TODO disable inputs while dashing?
                var position = localTransform.ValueRO.Position;
                var target = dash.ValueRO.NextPosition;
                var next = math.lerp(position, target, math.saturate(deltaTime * MoveRate));
                // On the floor where there is some; over water or a gap it glides at the path's height
                float bottom = UnitMovement.BottomOffset(collider.ValueRO, localTransform.ValueRO.Rotation, localTransform.ValueRO.Scale);
                if (UnitMovement.TryFindFloor(world, next, bottom, config.StepHeight, config.StepHeight + 0.5f, FloorLayers(ref state, entity), out float floorY))
                    next.y = floorY + bottom + UnitMovement.Skin;
                localTransform.ValueRW.Position = next;

                if(math.distance(next.xz, target.xz) < 0.3f)
                {
                    dash.ValueRW.Dashing = false;
                }
            }
        }

        foreach (var (dash, localTransform, desiredDirection, collider, skillBuffer, entity) in
                 SystemAPI.Query<RefRW<Dash>, RefRW<LocalTransform>, RefRO<DesiredDirection>, RefRO<PhysicsCollider>, DynamicBuffer<Skill>>().WithEntityAccess())
        {
            if(dash.ValueRO.Elapsed >= SkillUtil.Cooldown(dash.ValueRO.Cooldown, SystemAPI.GetComponent<CalculatedStats>(entity).Value))
            {
                localTransform.ValueRW.Rotation = desiredDirection.ValueRO.Quaternion;
                dash.ValueRW.Elapsed = 0.0f;
                dash.ValueRW.Dashing = true;
                dash.ValueRW.NextPosition = DashEnd(world, collider.ValueRO, localTransform.ValueRO, entity, desiredDirection.ValueRO.Direction,
                                                    minWalkableNormalY, config.StepHeight, FloorLayers(ref state, entity));

                // A use (tagged Mobility, so it also raises MobilitySkillUsed for e.g. Refractionist)
                SkillUtil.MarkUsed(skillBuffer, SkillType.Dash);
            }

        }

        // Report cooldown and dashing state (after SkillActivationSystem's defaults)
        foreach (var (dash, skillBuffer, stats) in SystemAPI.Query<RefRO<Dash>, DynamicBuffer<Skill>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            int index = -1;
            if (SkillUtil.FindSkill(skillBuffer, SkillType.Dash, ref index))
            {
                ref var skill = ref skillBuffer.ElementAt(index);
                float cooldown = SkillUtil.Cooldown(dash.ValueRO.Cooldown, stats.ValueRO.Value);
                skill.State.CooldownDuration = cooldown;
                skill.State.CooldownRemaining = math.max(0f, cooldown - dash.ValueRO.Elapsed);
                skill.State.IsActive = dash.ValueRO.Dashing;
            }
        }
    }

    // Water counts as floor for units that can walk on it
    uint FloorLayers(ref SystemState state, Entity entity)
    {
        bool waterWalking = SystemAPI.HasComponent<WaterWalking>(entity) && SystemAPI.IsComponentEnabled<WaterWalking>(entity);
        return UnitMovement.GroundLayer | (waterWalking ? (uint)CollisionLayerHelper.Water : 0u);
    }

    // As far as Distance allows before a wall (enemies don't stop it), then back along the path to the last spot with floor
    static float3 DashEnd(in CollisionWorld world, in PhysicsCollider collider, in LocalTransform transform, Entity self, float3 direction,
                          float minWalkableNormalY, float stepHeight, uint floorLayers)
    {
        var start = transform.Position;
        var flat = math.normalizesafe(new float3(direction.x, 0f, direction.z));
        var move = flat * Distance;
        var hits = new NativeList<ColliderCastHit>(8, Allocator.Temp);
        float fraction = UnitMovement.Sweep(world, collider, start, move, transform.Rotation, transform.Scale, self,
                                            UnitMovement.WorldLayers, minWalkableNormalY, ref hits, out _);
        float reach = math.max(0f, Distance * fraction - (fraction < 1f ? UnitMovement.Skin : 0f));

        float bottom = UnitMovement.BottomOffset(collider, transform.Rotation, transform.Scale);
        for (float distance = reach; distance > 0f; distance -= BackOffStep)
        {
            var end = start + flat * distance;
            // Generous height range: the far side of a gap can be a little higher or lower than the start
            if (UnitMovement.TryFindFloor(world, end, bottom, stepHeight + 1f, stepHeight + 2f, floorLayers, out float floorY))
                return new float3(end.x, floorY + bottom + UnitMovement.Skin, end.z);
        }
        return start;
    }
}
