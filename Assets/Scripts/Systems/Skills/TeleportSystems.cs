using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Teleport: a short cast standing still, then the unit appears at the cursor (no further than Range). Walls don't stop
/// it: it lands anywhere with floor where its collider fits, pulled back toward the caster from the cursor until there
/// is such a spot. The spot is picked when the cast starts; with none, or not enough resource, it isn't cast. While
/// casting the unit doesn't move (UnitMoverSystem skips it).
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial struct TeleportSystem : ISystem
{
    const float BackOffStep = 0.25f;    // Meters per step when pulling the landing spot back toward the caster
    const float FloorSearch = 2f;       // Meters above and below the expected height to look for floor at the landing spot

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

        foreach (var (teleport, localTransform, skillBuffer) in SystemAPI.Query<RefRW<Teleport>, RefRW<LocalTransform>, DynamicBuffer<Skill>>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            teleport.ValueRW.Elapsed = math.min(teleport.ValueRO.Elapsed + deltaTime, teleport.ValueRO.Cooldown);

            if (teleport.ValueRO.Casting)
            {
                teleport.ValueRW.CastElapsed += deltaTime;
                if (teleport.ValueRO.CastElapsed >= teleport.ValueRO.CastTime)
                {
                    localTransform.ValueRW.Position = teleport.ValueRO.Target;
                    teleport.ValueRW.Casting = false;

                    // A use on landing (tagged Mobility, so it also raises MobilitySkillUsed for e.g. Refractionist)
                    SkillUtil.MarkUsed(skillBuffer, SkillType.Teleport);
                }
            }
        }

        foreach (var (teleport, localTransform, desiredDirection, inputs, collider, resource, entity) in
                 SystemAPI.Query<RefRW<Teleport>, RefRW<LocalTransform>, RefRO<DesiredDirection>, RefRO<InputsData>, RefRO<PhysicsCollider>, RefRO<Resource>>()
                          .WithEntityAccess())
        {
            if (teleport.ValueRO.Casting || teleport.ValueRO.Elapsed < teleport.ValueRO.Cooldown) continue;
            if (resource.ValueRO.CurrentValue < teleport.ValueRO.ResourceCost) continue;
            if (!TryFindLanding(world, collider.ValueRO, localTransform.ValueRO, entity, inputs.ValueRO.MousePosition, teleport.ValueRO.Range,
                                minWalkableNormalY, config.StepHeight, FloorLayers(ref state, entity), out var target))
                continue;

            localTransform.ValueRW.Rotation = desiredDirection.ValueRO.Quaternion;
            teleport.ValueRW.Elapsed = 0f;
            teleport.ValueRW.Casting = true;
            teleport.ValueRW.CastElapsed = 0f;
            teleport.ValueRW.Target = target;
            SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger{Value = -teleport.ValueRO.ResourceCost});
        }

        // Report cooldown and casting state (after SkillActivationSystem's defaults)
        foreach (var (teleport, skillBuffer) in SystemAPI.Query<RefRO<Teleport>, DynamicBuffer<Skill>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            int index = -1;
            if (SkillUtil.FindSkill(skillBuffer, SkillType.Teleport, ref index))
            {
                ref var skill = ref skillBuffer.ElementAt(index);
                skill.State.CooldownDuration = teleport.ValueRO.Cooldown;
                skill.State.CooldownRemaining = math.max(0f, teleport.ValueRO.Cooldown - teleport.ValueRO.Elapsed);
                skill.State.IsActive = teleport.ValueRO.Casting;
            }
        }
    }

    // Water counts as floor for units that can walk on it
    uint FloorLayers(ref SystemState state, Entity entity)
    {
        bool waterWalking = SystemAPI.HasComponent<WaterWalking>(entity) && SystemAPI.IsComponentEnabled<WaterWalking>(entity);
        return UnitMovement.GroundLayer | (waterWalking ? (uint)CollisionLayerHelper.Water : 0u);
    }

    // The cursor (no further than range), else the closest spot back toward the caster with floor where the collider fits
    static bool TryFindLanding(in CollisionWorld world, in PhysicsCollider collider, in LocalTransform transform, Entity self, float3 cursor, float range,
                               float minWalkableNormalY, float stepHeight, uint floorLayers, out float3 landing)
    {
        landing = float3.zero;
        var start = transform.Position;
        var offset = cursor.xz - start.xz;
        float length = math.length(offset);
        if (length < 1e-3f) return false;
        var direction = offset / length;

        float bottom = UnitMovement.BottomOffset(collider, transform.Rotation, transform.Scale);
        for (float distance = math.min(length, range); distance > 0f; distance -= BackOffStep)
        {
            // Expected height: from the caster's toward the cursor's, so it can land on higher or lower ground
            var spot = new float3(start.x + direction.x * distance, math.lerp(start.y, cursor.y, distance / length), start.z + direction.y * distance);
            if (!UnitMovement.TryFindFloor(world, spot, bottom, stepHeight + FloorSearch, stepHeight + FloorSearch, floorLayers, out float floorY))
                continue;
            spot.y = floorY + bottom + UnitMovement.Skin;
            if (!Fits(world, collider, spot, transform.Rotation, transform.Scale, self, minWalkableNormalY)) continue;
            landing = spot;
            return true;
        }
        return false;
    }

    // Not inside a wall, prop or steep ground there (enemies are fine: UnitMoverSystem pushes them apart)
    static bool Fits(in CollisionWorld world, in PhysicsCollider collider, float3 position, quaternion rotation, float scale, Entity self, float minWalkableNormalY)
    {
        if (!collider.IsValid) return true;
        var distances = new NativeList<DistanceHit>(8, Allocator.Temp);
        world.CalculateDistance(new ColliderDistanceInput(collider.Value, 0f, new RigidTransform(rotation, position), scale), ref distances);
        foreach (var hit in distances)
        {
            if (hit.Entity == self || hit.Distance >= 0f) continue;
            uint layer = world.Bodies[hit.RigidBodyIndex].Collider.Value.GetCollisionFilter(hit.ColliderKey).BelongsTo;
            if ((layer & UnitMovement.WorldLayers) != 0 || ((layer & UnitMovement.GroundLayer) != 0 && hit.SurfaceNormal.y < minWalkableNormalY))
                return false;
        }
        return true;
    }
}
