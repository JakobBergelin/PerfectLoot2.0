using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Companion movement, as a flock around each owner. Each companion keeps to its own area in front of, behind, beside or
/// around its owner, seen from the way the owner last walked, or heads for its target while that target is within Leash
/// of the owner. While the owner walks, the flock moves along with them and pulls together. Walks along the owner's
/// FlowField, runs faster the further it has to go, steers away from nearby units and teleports back past
/// TeleportDistance. Stands still while casting or when it can't act. Only writes the wanted horizontal velocity
/// (UnitMotion); UnitMoverSystem moves it.
/// </summary>
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateAfter(typeof(UnitSpatialGridSystem))]
[UpdateAfter(typeof(CompanionDetectionSystem))]
[UpdateBefore(typeof(UnitMoverSystem))]
[BurstCompile]
public partial struct BoidSystem : ISystem
{
    const float MovingSpeedSq = 0.01f;  // The owner counts as walking above this
    const float MinSpread = 0.2f;       // Close enough to where it wants to be, however small its area
    const float SlowDistance = 1f;      // It slows down this close to where it stops
    const float CatchUpDistance = 6f;   // Its top speed from this far away
    const float TurnRate = 10f;
    const float Cohesion = 0.2f;        // Meters per second toward the flock's center, per meter away from it

    /// <summary>One owner's companions that aren't chasing anything, added up.</summary>
    struct Flock
    {
        public float3 Position;
        public float3 Velocity;
        public int Count;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Boid>();
        state.RequireForUpdate<UnitSpatialGrid>();
        state.RequireForUpdate<FlowFields>();
        state.RequireForUpdate<MovementConfig>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var fields = SystemAPI.GetSingleton<FlowFields>();
        var grid = SystemAPI.GetSingleton<UnitSpatialGrid>().Units;
        var config = SystemAPI.GetSingleton<MovementConfig>();
        var world = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        var targetables = SystemAPI.GetComponentLookup<EnemyTargetable>(true);
        float minWalkableNormalY = math.cos(math.radians(config.MaxSlope));
        float deltaTime = SystemAPI.Time.DeltaTime;
        state.CompleteDependency();  // The flow fields and the grid are used here on the main thread

        var flocks = new NativeHashMap<Entity, Flock>(8, Allocator.Temp);
        foreach (var (boid, companion, transform, motion) in SystemAPI.Query<RefRO<Boid>, RefRO<Companion>, RefRO<LocalTransform>, RefRO<UnitMotion>>())
        {
            if (boid.ValueRO.Chasing) continue;
            var owner = companion.ValueRO.Owner;
            flocks.TryGetValue(owner, out var flock);
            flock.Position += transform.ValueRO.Position;
            flock.Velocity += motion.ValueRO.Actual;
            flock.Count++;
            flocks[owner] = flock;
        }

        foreach (var (boidRef, companion, transformRef, motion, target, steering, collider, entity) in
                 SystemAPI.Query<RefRW<Boid>, RefRO<Companion>, RefRW<LocalTransform>, RefRW<UnitMotion>, RefRO<EnemyTarget>, RefRO<Steering>, RefRO<PhysicsCollider>>().WithEntityAccess())
        {
            motion.ValueRW.Velocity = float3.zero;
            var owner = companion.ValueRO.Owner;
            var modifiers = SystemAPI.GetComponent<MovementModifiers>(entity);
            if (modifiers.PreventsActions || !targetables.HasComponent(owner)) continue;

            ref var boid = ref boidRef.ValueRW;
            ref var transform = ref transformRef.ValueRW;
            var settings = boid.Settings;
            var ownerTransform = SystemAPI.GetComponent<LocalTransform>(owner);
            var ownerPosition = ownerTransform.Position;

            var walking = SystemAPI.GetComponent<UnitMotion>(owner).Actual;
            walking.y = 0f;
            bool ownerWalks = math.lengthsq(walking) > MovingSpeedSq;
            if (ownerWalks) boid.Heading = math.normalize(walking);
            var heading = math.normalizesafe(boid.Heading, FlatForward(ownerTransform.Rotation));
            var area = AreaCenter(settings, ownerPosition, heading);
            float spread = math.max(settings.Spread, MinSpread);

            if (math.distancesq(transform.Position.xz, ownerPosition.xz) > settings.TeleportDistance * settings.TeleportDistance)
            {
                // To the edge of its area on its own side, so a group doesn't land on the same spot
                var edge = area + math.normalizesafe(new float3(transform.Position.x - area.x, 0f, transform.Position.z - area.z), -heading) * spread;
                if (TeleportSystem.TryFindLanding(world, collider.ValueRO, transform.WithPosition(ownerPosition), entity, edge, settings.Distance + spread,
                                                  minWalkableNormalY, config.StepHeight, UnitMovement.GroundLayer, out var landing))
                    transform.Position = landing;
                continue;
            }

            // Standing still while casting keeps the aim of the skill
            if (SystemAPI.GetComponent<NpcBehavior>(entity).IsCasting) continue;

            var unit = target.ValueRO.Unit;
            var unitPosition = SystemAPI.HasComponent<LocalTransform>(unit) ? SystemAPI.GetComponent<LocalTransform>(unit).Position : float3.zero;
            bool chasing = unit != Entity.Null && math.distancesq(unitPosition.xz, ownerPosition.xz) <= settings.Leash * settings.Leash;
            boid.Chasing = chasing;
            var destination = chasing ? unitPosition : area;
            float stopDistance = chasing ? settings.AttackDistance : spread;

            var position = transform.Position;
            float distance = math.distance(position.xz, destination.xz) - stopDistance;
            // Its own stat is a bonus on the base speed, as for players
            float speed = config.MovementSpeed * (1f + SystemAPI.GetComponent<MovementSpeed>(entity).Value) * modifiers.SpeedMultiplier;
            float topSpeed = speed * math.lerp(1f, settings.CatchUpMultiplier, math.saturate(distance / CatchUpDistance));

            var desired = float3.zero;
            // In its area it looks the way its owner walks, at its target at the target
            var facing = chasing ? math.normalizesafe(new float3(destination.x - position.x, 0f, destination.z - position.z), heading) : heading;
            if (distance > 0f)
            {
                facing = fields[targetables[owner].FieldSlot].Direction(position, destination);
                desired = facing * topSpeed * math.saturate(distance / SlowDistance);
            }
            if (!chasing && ownerWalks && flocks.TryGetValue(owner, out var flock))
                desired += FlockPull(flock, position, walking);
            transform.Rotation = math.slerp(transform.Rotation, quaternion.LookRotationSafe(facing, math.up()), math.saturate(TurnRate * deltaTime));

            desired += UnitSpatialGrid.Separation(grid, entity, position, steering.ValueRO.Radius, config.SeparationSpacing) * config.SeparationStrength(steering.ValueRO.BodyType);

            // The push may exceed the walk speed a little, as for enemies
            float maxSpeed = math.max(topSpeed, 1f) * 1.5f;
            float desiredSpeed = math.length(desired);
            if (desiredSpeed > maxSpeed) desired *= maxSpeed / desiredSpeed;
            motion.ValueRW.Velocity = new float3(desired.x, 0f, desired.z);
        }
    }

    // Moves with the flock and pulls toward its center. The owner counts for half, so the flock sets off as soon as they do.
    static float3 FlockPull(in Flock flock, float3 position, float3 ownerVelocity)
    {
        var velocity = (flock.Velocity / flock.Count + ownerVelocity) * 0.5f;
        return velocity + (flock.Position / flock.Count - position) * Cohesion;
    }

    static float3 FlatForward(quaternion rotation)
    {
        var forward = math.forward(rotation);
        return math.normalizesafe(new float3(forward.x, 0f, forward.z), new float3(0f, 0f, 1f));
    }

    static float3 AreaCenter(in BoidSettings settings, float3 ownerPosition, float3 heading)
    {
        var right = new float3(heading.z, 0f, -heading.x);
        var side = settings.Position switch
        {
            BoidPosition.Front => heading,
            BoidPosition.Left => -right,
            BoidPosition.Right => right,
            BoidPosition.Around => float3.zero,
            _ => -heading,
        };
        return ownerPosition + side * settings.Distance;
    }
}
