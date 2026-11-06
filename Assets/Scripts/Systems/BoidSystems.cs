using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Companion movement, as a loose flock around each owner. Each companion has its own spot in its area in front of, behind,
/// beside or around its owner, seen from the way the owner walks, and picks a new one now and then. It sets off once its
/// spot is more than its Slack away, so it follows a moment after its owner, and runs faster the further it has to go.
/// Companions walking together match each other a little and pull together. A target within Leash of the owner is chased
/// instead; enemies within FleeDistance send it to a spot on the safe side of its area. Walks along the owner's FlowField,
/// steers away from nearby units and teleports back past TeleportDistance. Stands still while casting or when it can't
/// act. Only writes the wanted horizontal velocity (UnitMotion); UnitMoverSystem moves it.
/// </summary>
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateAfter(typeof(UnitSpatialGridSystem))]
[UpdateAfter(typeof(CompanionDetectionSystem))]
[UpdateBefore(typeof(UnitMoverSystem))]
[BurstCompile]
public partial struct BoidSystem : ISystem
{
    const float MovingSpeedSq = 0.01f;  // The owner counts as walking above this
    const float ArriveDistance = 0.3f;  // Close enough to where it wants to be
    const float SlowDistance = 1f;      // It slows down this close to where it stops
    const float CatchUpDistance = 6f;   // Its top speed from this far away
    const float StrollPace = 0.4f;      // Share of its speed right next to its spot
    const float Acceleration = 15f;     // Meters per second, per second
    const float TurnRate = 10f;
    const float HeadingTurnRate = 2f;   // How quickly areas swing around when the owner turns
    const float Alignment = 0.3f;       // Share of the flock's velocity it takes on
    const float Cohesion = 0.2f;        // Meters per second toward the flock's center, per meter away from it
    const float MinSlack = 1.5f;        // Meters its spot gets away before it sets off
    const float MaxSlack = 3.5f;
    const float MinSpotTime = 3f;       // Seconds before it picks a new spot
    const float MaxSpotTime = 7f;
    const float FleeMargin = 1.5f;      // Meters beyond FleeDistance where it stops fleeing
    const float FleeSpotTime = 1f;      // Seconds it keeps a spot while fleeing

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
        var enemies = SystemAPI.GetComponentLookup<Enemy>(true);
        float minWalkableNormalY = math.cos(math.radians(config.MaxSlope));
        float deltaTime = SystemAPI.Time.DeltaTime;
        state.CompleteDependency();  // The flow fields and the grid are used here on the main thread

        var flocks = new NativeHashMap<Entity, Flock>(8, Allocator.Temp);
        foreach (var (boid, companion, transform, motion) in SystemAPI.Query<RefRO<Boid>, RefRO<Companion>, RefRO<LocalTransform>, RefRO<UnitMotion>>().WithNone<SpawnRise, RigidDeath>())
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
                 SystemAPI.Query<RefRW<Boid>, RefRO<Companion>, RefRW<LocalTransform>, RefRW<UnitMotion>, RefRO<EnemyTarget>, RefRO<Steering>, RefRO<PhysicsCollider>>().WithNone<SpawnRise>().WithEntityAccess())
        {
            ref var boid = ref boidRef.ValueRW;
            ref var transform = ref transformRef.ValueRW;
            motion.ValueRW.Velocity = float3.zero;
            var owner = companion.ValueRO.Owner;
            var modifiers = SystemAPI.GetComponent<MovementModifiers>(entity);
            if (modifiers.PreventsActions || !targetables.HasComponent(owner))
            {
                boid.Velocity = float3.zero;
                continue;
            }

            if (boid.Random.state == 0) boid.Random = Random.CreateFromIndex((uint)entity.Index);
            var settings = boid.Settings;
            var position = transform.Position;
            var ownerTransform = SystemAPI.GetComponent<LocalTransform>(owner);
            var ownerPosition = ownerTransform.Position;

            var walking = SystemAPI.GetComponent<UnitMotion>(owner).Actual;
            walking.y = 0f;
            if (math.lengthsq(walking) > MovingSpeedSq) TurnHeading(ref boid.Heading, math.normalize(walking), deltaTime);
            var heading = math.normalizesafe(boid.Heading, FlatForward(ownerTransform.Rotation));

            var away = float3.zero;
            if (settings.FleeDistance > 0f)
                away = UnitSpatialGrid.AwayFromEnemies(grid, enemies, position, settings.FleeDistance + (boid.Fleeing ? FleeMargin : 0f));
            bool fleeing = math.lengthsq(away) > 0f;
            UpdateSpot(ref boid, settings, away, fleeing, heading, deltaTime);
            var spot = AreaCenter(settings, ownerPosition, heading) + boid.Spot;

            if (math.distancesq(position.xz, ownerPosition.xz) > settings.TeleportDistance * settings.TeleportDistance)
            {
                boid.Velocity = float3.zero;
                if (TeleportSystem.TryFindLanding(world, collider.ValueRO, transform.WithPosition(ownerPosition), entity, spot, settings.Distance + settings.Spread + ArriveDistance,
                                                  minWalkableNormalY, config.StepHeight, UnitMovement.GroundLayer, out var landing))
                    transform.Position = landing;
                continue;
            }

            // Standing still while casting keeps the aim of the skill
            var behavior = SystemAPI.GetComponent<NpcBehavior>(entity);
            if (behavior.IsCasting)
            {
                boid.Velocity = float3.zero;
                continue;
            }

            var unit = target.ValueRO.Unit;
            var unitPosition = SystemAPI.HasComponent<LocalTransform>(unit) ? SystemAPI.GetComponent<LocalTransform>(unit).Position : float3.zero;
            bool chasing = unit != Entity.Null && math.distancesq(unitPosition.xz, ownerPosition.xz) <= settings.Leash * settings.Leash;
            boid.Chasing = chasing;
            var destination = chasing ? unitPosition : spot;
            float stopDistance = chasing ? settings.AttackDistance : ArriveDistance;
            float distance = math.distance(position.xz, destination.xz);

            // Following: it lets its spot get away first, then goes all the way
            if (distance > boid.Slack || fleeing) boid.Moving = true;
            else if (distance <= ArriveDistance) boid.Moving = false;
            bool moving = chasing ? distance > stopDistance : boid.Moving;

            // Its own stat is a bonus on the base speed, as for players
            float speed = config.MovementSpeed * (1f + SystemAPI.GetComponent<MovementSpeed>(entity).Value) * modifiers.SpeedMultiplier;
            float nearPace = chasing || fleeing ? 1f : StrollPace;
            float topSpeed = speed * math.lerp(nearPace, settings.CatchUpMultiplier, math.saturate(distance / CatchUpDistance));

            var desired = float3.zero;
            // Waiting it looks the way its owner walks, at its target at the target
            var facing = chasing ? math.normalizesafe(new float3(destination.x - position.x, 0f, destination.z - position.z), heading) : heading;
            if (moving)
            {
                facing = fields[targetables[owner].FieldSlot].Direction(position, destination);
                desired = facing * topSpeed * math.saturate((distance - stopDistance) / SlowDistance);
                if (!chasing && !fleeing && flocks.TryGetValue(owner, out var flock))
                    desired += FlockPull(flock, position);
            }
            if (!behavior.IsAiming) transform.Rotation = math.slerp(transform.Rotation, quaternion.LookRotationSafe(facing, math.up()), math.saturate(TurnRate * deltaTime));

            desired += UnitSpatialGrid.Separation(grid, entity, position, steering.ValueRO.Radius, config.SeparationSpacing) * config.SeparationStrength(steering.ValueRO.BodyType);

            // The push may exceed the walk speed a little, as for enemies
            float maxSpeed = math.max(topSpeed, 1f) * 1.5f;
            float desiredSpeed = math.length(desired);
            if (desiredSpeed > maxSpeed) desired *= maxSpeed / desiredSpeed;

            boid.Velocity = MoveTowards(boid.Velocity, new float3(desired.x, 0f, desired.z), Acceleration * deltaTime);
            motion.ValueRW.Velocity = boid.Velocity;
        }
    }

    // A new spot when its time is up. Fleeing: the safe side of its area, kept a moment so it doesn't flip between enemies.
    static void UpdateSpot(ref Boid boid, in BoidSettings settings, float3 away, bool fleeing, float3 heading, float deltaTime)
    {
        boid.SpotTime -= deltaTime;
        if (fleeing && (!boid.Fleeing || boid.SpotTime <= 0f))
        {
            boid.Spot = math.normalizesafe(away, -heading) * OuterRadius(settings);
            boid.SpotTime = FleeSpotTime;
        }
        else if (!fleeing && boid.Fleeing)
        {
            boid.SpotTime = MaxSpotTime;  // Stays on the safe side for now
        }
        else if (!fleeing && boid.SpotTime <= 0f)
        {
            float angle = boid.Random.NextFloat(2f * math.PI);
            float inner = settings.Position == BoidPosition.Around ? settings.Distance : 0f;
            boid.Spot = new float3(math.sin(angle), 0f, math.cos(angle)) * boid.Random.NextFloat(inner, OuterRadius(settings));
            boid.SpotTime = boid.Random.NextFloat(MinSpotTime, MaxSpotTime);
            boid.Slack = boid.Random.NextFloat(MinSlack, MaxSlack);
            boid.Moving = true;
        }
        boid.Fleeing = fleeing;
    }

    static float OuterRadius(in BoidSettings settings)
    {
        float spread = math.max(settings.Spread, ArriveDistance);
        return settings.Position == BoidPosition.Around ? settings.Distance + spread : spread;
    }

    static void TurnHeading(ref float3 heading, float3 wanted, float deltaTime)
    {
        heading = math.lengthsq(heading) > 0f ? math.normalizesafe(math.lerp(heading, wanted, math.saturate(HeadingTurnRate * deltaTime)), wanted) : wanted;
    }

    static float3 FlockPull(in Flock flock, float3 position)
    {
        return flock.Velocity / flock.Count * Alignment + (flock.Position / flock.Count - position) * Cohesion;
    }

    static float3 MoveTowards(float3 from, float3 to, float maxChange)
    {
        var change = to - from;
        float length = math.length(change);
        return length <= maxChange ? to : from + change / length * maxChange;
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
