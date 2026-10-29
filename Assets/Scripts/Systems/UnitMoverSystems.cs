using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Physics queries for moving units without the physics solver: sweeping a unit's collider to find what blocks it,
/// and finding the floor under it. Shared by UnitMoverSystem, DashSystem, TeleportSystem and BallLightningSystem.
/// </summary>
public static class UnitMovement
{
    /// <summary>Gap kept between a unit and what it slides along, and between its collider's bottom and the floor.</summary>
    public const float Skin = 0.05f;

    /// <summary>What blocks every unit: walls and props (Default). Steep ground blocks too (see Sweep).</summary>
    public const uint WorldLayers = (uint)CollisionLayerHelper.Wall | 1u;
    public const uint GroundLayer = (uint)CollisionLayerHelper.Ground;

    /// <summary>How far below its position a unit's collider reaches, at its rotation and scale.</summary>
    public static float BottomOffset(in PhysicsCollider collider, quaternion rotation, float scale)
    {
        if (!collider.IsValid) return 0f;
        var aabb = collider.Value.Value.CalculateAabb(new RigidTransform(rotation, float3.zero), scale);
        return math.max(0f, -aabb.Min.y);
    }

    /// <summary>
    /// The floor height under a unit whose collider reaches bottomOffset below position: a ray down from stepUp above
    /// its bottom to stepDown below it, against floorLayers. False when there's no floor there (water, an empty cell).
    /// </summary>
    public static bool TryFindFloor(in CollisionWorld world, float3 position, float bottomOffset, float stepUp, float stepDown,
                                    uint floorLayers, out float floorY)
    {
        float bottom = position.y - bottomOffset;
        var input = new RaycastInput
        {
            Start = new float3(position.x, bottom + stepUp, position.z),
            End = new float3(position.x, bottom - stepDown, position.z),
            Filter = new CollisionFilter { BelongsTo = ~0u, CollidesWith = floorLayers },
        };
        bool found = world.CastRay(input, out var hit);
        floorY = found ? hit.Position.y : 0f;
        return found;
    }

    /// <summary>
    /// Sweeps the collider from start along move and returns how much of the move is free (0-1) and the blocking surface's
    /// normal. Blocks are hits on blockLayers, and ground (GroundLayer) steeper than the walkable slope. The unit's own
    /// body is ignored.
    /// </summary>
    public static float Sweep(in CollisionWorld world, in PhysicsCollider collider, float3 start, float3 move, quaternion rotation, float scale,
                              Entity self, uint blockLayers, float minWalkableNormalY, ref NativeList<ColliderCastHit> hits, out float3 normal)
    {
        normal = float3.zero;
        if (!collider.IsValid || math.lengthsq(move) < 1e-10f) return 1f;
        hits.Clear();
        world.CastCollider(new ColliderCastInput(collider.Value, start, start + move, rotation, scale), ref hits);

        float best = 1f;
        foreach (var hit in hits)
        {
            if (hit.Entity == self || hit.Fraction >= best) continue;
            uint layer = world.Bodies[hit.RigidBodyIndex].Collider.Value.GetCollisionFilter(hit.ColliderKey).BelongsTo;
            bool blocks = (layer & blockLayers) != 0 || ((layer & GroundLayer) != 0 && hit.SurfaceNormal.y < minWalkableNormalY);
            if (!blocks) continue;
            best = hit.Fraction;
            normal = hit.SurfaceNormal;
        }
        return best;
    }
}

/// <summary>
/// Moves every unit (player and enemies) by its wanted velocity (UnitMotion) plus knockback. Units are kinematic, so
/// nothing else moves them: overlaps are pushed apart gently, the collider is swept so it slides along walls, props,
/// steep ground and blocking units (the player and Physics-body enemies block each other unless either is Phased;
/// Swarm only hits the world),
/// and it follows the floor up steps and hills. Spots without floor (water, empty cells, the map edge) are never
/// entered, unless WaterWalking makes water count as floor. Skips rising (SpawnRise), dashing and teleporting units.
/// </summary>
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateAfter(typeof(EnemyMovementSystem))]
[UpdateAfter(typeof(CharacterMovementSystem))]
[BurstCompile]
public partial struct UnitMoverSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<MovementConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<MovementConfig>();
        state.Dependency = new MoveJob
        {
            World = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld,
            Players = SystemAPI.GetComponentLookup<Player>(true),
            Dashes = SystemAPI.GetComponentLookup<Dash>(true),
            Teleports = SystemAPI.GetComponentLookup<Teleport>(true),
            BallLightnings = SystemAPI.GetComponentLookup<BallLightning>(true),
            Phaseds = SystemAPI.GetComponentLookup<Phased>(true),
            PlayerPhased = !SystemAPI.QueryBuilder().WithAll<Player, Phased>().Build().IsEmpty,
            WaterWalkers = SystemAPI.GetComponentLookup<WaterWalking>(true),
            Knockbacks = SystemAPI.GetComponentLookup<Knockback>(true),
            DeltaTime = SystemAPI.Time.DeltaTime,
            StepHeight = config.StepHeight,
            MinWalkableNormalY = math.cos(math.radians(config.MaxSlope)),
            MaxSlopeTangent = math.tan(math.radians(math.clamp(config.MaxSlope, 0f, 85f))),
            OverlapPushSpeed = config.OverlapPushSpeed,
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithNone(typeof(SpawnRise))]
    partial struct MoveJob : IJobEntity
    {
        const int SlideIterations = 3;

        [ReadOnly] public CollisionWorld World;
        [ReadOnly] public ComponentLookup<Player> Players;
        [ReadOnly] public ComponentLookup<Dash> Dashes;
        [ReadOnly] public ComponentLookup<Teleport> Teleports;
        [ReadOnly] public ComponentLookup<BallLightning> BallLightnings;
        [ReadOnly] public ComponentLookup<Phased> Phaseds;
        [ReadOnly] public ComponentLookup<WaterWalking> WaterWalkers;
        [ReadOnly] public ComponentLookup<Knockback> Knockbacks;
        public bool PlayerPhased;   // Enemies move through the player while it's phased
        public float DeltaTime, StepHeight, MinWalkableNormalY, MaxSlopeTangent, OverlapPushSpeed;

        void Execute(Entity entity, ref LocalTransform transform, ref UnitMotion motion, in PhysicsCollider collider, in Steering steering)
        {
            if (DeltaTime <= 0f) return;
            // A dash or Ball Lightning moves the unit itself (DashSystem, BallLightningSystem); a teleport stands still until it lands (TeleportSystem)
            if ((Dashes.HasComponent(entity) && Dashes[entity].Dashing) || (Teleports.HasComponent(entity) && Teleports[entity].Casting) ||
                (BallLightnings.HasComponent(entity) && BallLightnings[entity].Flying))
            {
                motion.Actual = float3.zero;
                return;
            }

            var velocity = motion.Velocity;
            if (Knockbacks.HasComponent(entity)) velocity += Knockbacks[entity].Velocity;
            var move = new float3(velocity.x, 0f, velocity.z) * DeltaTime;

            bool isPlayer = Players.HasComponent(entity);
            // Phased: other units don't block (nor push it out of them)
            bool phased = Phaseds.HasComponent(entity) && Phaseds.IsComponentEnabled(entity);
            uint blockLayers = UnitMovement.WorldLayers |
                               (phased ? 0u
                                : isPlayer ? (uint)CollisionLayerHelper.Enemy
                                : steering.BodyType == BodyType.Physics && !PlayerPhased ? (uint)CollisionLayerHelper.Player : 0u);
            uint floorLayers = UnitMovement.GroundLayer |
                               (WaterWalkers.HasComponent(entity) && WaterWalkers.IsComponentEnabled(entity) ? (uint)CollisionLayerHelper.Water : 0u);

            var rotation = transform.Rotation;
            float scale = transform.Scale;
            float bottom = UnitMovement.BottomOffset(collider, rotation, scale);
            var start = transform.Position;
            var position = start;
            var hits = new NativeList<ColliderCastHit>(8, Allocator.Temp);

            // Overlapping something already (spawned on top of the player, pushed into a wall): out gently
            position += Depenetrate(entity, collider, position, rotation, scale, blockLayers);

            // Collide and slide
            for (int i = 0; i < SlideIterations && math.lengthsq(move) > 1e-10f; i++)
            {
                float fraction = UnitMovement.Sweep(World, collider, position, move, rotation, scale, entity, blockLayers, MinWalkableNormalY, ref hits, out var normal);
                float length = math.length(move);
                var step = move * fraction;
                if (fraction < 1f) step -= move / length * math.min(UnitMovement.Skin, length * fraction);

                // Already touching what blocks it: nothing to step, straight to sliding along it. Otherwise only onto
                // floor; along a water or map edge (at any angle), the step turned along the edge
                if (math.lengthsq(step) > 1e-10f && !TryStepAlongEdge(ref position, step, bottom, floorLayers))
                    break;
                if (fraction >= 1f) break;

                // Slide: what's left of the move, along the blocking surface
                var flatNormal = math.normalizesafe(new float3(normal.x, 0f, normal.z));
                move *= 1f - fraction;
                move -= math.dot(move, flatNormal) * flatNormal;
            }

            // Standing still still follows the floor (no floor under it at all: stays where it is)
            if (UnitMovement.TryFindFloor(World, position, bottom, StepHeight, StepHeight + 0.1f, floorLayers, out float floorY))
                position.y = floorY + bottom + UnitMovement.Skin;

            transform.Position = position;
            motion.Actual = new float3(position.x - start.x, 0f, position.z - start.z) / DeltaTime;
        }

        // The step if it ends on floor; otherwise turned 30°, 60° or 90° to either side (shortened to the part that goes that
        // way), the smallest turn that stays on floor, so units slide along water and map edges; else half the step
        bool TryStepAlongEdge(ref float3 position, float3 step, float bottom, uint floorLayers)
        {
            if (TryStep(ref position, step, bottom, floorLayers)) return true;
            for (int degrees = 30; degrees <= 90; degrees += 30)
            {
                float angle = math.radians(degrees);
                float length = math.cos(angle);
                for (int side = -1; side <= 1; side += 2)
                {
                    var turned = math.mul(quaternion.RotateY(angle * side), step) * length;
                    if (TryStep(ref position, turned, bottom, floorLayers)) return true;
                }
            }
            return TryStep(ref position, step * 0.5f, bottom, floorLayers);
        }

        // Moves by step when there's floor at the end (stepping up or down to it); false (unmoved) otherwise
        bool TryStep(ref float3 position, float3 step, float bottom, uint floorLayers)
        {
            if (math.lengthsq(step) < 1e-12f) return false;
            var target = position + step;
            float down = StepHeight + math.length(step.xz) * MaxSlopeTangent + 0.1f;
            if (!UnitMovement.TryFindFloor(World, target, bottom, StepHeight, down, floorLayers, out float floorY)) return false;
            target.y = floorY + bottom + UnitMovement.Skin;
            position = target;
            return true;
        }

        // Horizontal push out of walls, props, steep ground and blocking units it overlaps, at most OverlapPushSpeed
        float3 Depenetrate(Entity self, in PhysicsCollider collider, float3 position, quaternion rotation, float scale, uint blockLayers)
        {
            if (!collider.IsValid) return float3.zero;
            var distances = new NativeList<DistanceHit>(8, Allocator.Temp);
            World.CalculateDistance(new ColliderDistanceInput(collider.Value, 0f, new RigidTransform(rotation, position), scale), ref distances);
            var push = float3.zero;
            foreach (var hit in distances)
            {
                if (hit.Entity == self || hit.Distance >= 0f) continue;
                uint layer = World.Bodies[hit.RigidBodyIndex].Collider.Value.GetCollisionFilter(hit.ColliderKey).BelongsTo;
                bool blocks = (layer & blockLayers) != 0 || ((layer & UnitMovement.GroundLayer) != 0 && hit.SurfaceNormal.y < MinWalkableNormalY);
                if (!blocks) continue;
                push += new float3(hit.SurfaceNormal.x, 0f, hit.SurfaceNormal.z) * -hit.Distance;
            }
            float max = OverlapPushSpeed * DeltaTime;
            float length = math.length(push);
            return length > max ? push * (max / length) : push;
        }
    }
}
