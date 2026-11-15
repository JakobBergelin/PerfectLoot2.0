using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Event Horizon: captures the wearer's projectiles on the frame they spawn (still marked InitializeFromSource), then
/// releases the oldest per wearer beyond MaxProjectiles, and any whose wearer is gone or no longer has the power
/// (unequipped, died). First in MovementSystemGroup, so a new projectile orbits from its first move.
/// </summary>
[UpdateInGroup(typeof(MovementSystemGroup), OrderFirst = true)]
[BurstCompile]
public partial struct EventHorizonCaptureSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Capture
        var horizons = SystemAPI.GetComponentLookup<EventHorizon>();
        var commandBuffer = new EntityCommandBuffer(Allocator.Temp);
        foreach (var (source, entity) in SystemAPI.Query<RefRO<Source>>()
                     .WithAll<Projectile>().WithPresent<InitializeFromSource>().WithNone<Orbiting>().WithEntityAccess())
        {
            var wearer = source.ValueRO.Entity;
            if (!horizons.HasComponent(wearer)) continue;
            var horizon = horizons.GetRefRW(wearer);
            commandBuffer.AddComponent(entity, new Orbiting { Center = wearer, Order = horizon.ValueRO.NextOrder });
            horizon.ValueRW.NextOrder++;
        }
        commandBuffer.Playback(state.EntityManager);
        commandBuffer.Dispose();

        // Release: wearer gone or without the power, or more in orbit than allowed (oldest first)
        horizons = SystemAPI.GetComponentLookup<EventHorizon>(true);
        var query = SystemAPI.QueryBuilder().WithAll<Orbiting>().Build();
        var entities = query.ToEntityArray(Allocator.Temp);
        var orbits = query.ToComponentDataArray<Orbiting>(Allocator.Temp);
        var released = new NativeArray<bool>(entities.Length, Allocator.Temp);
        var counts = new NativeHashMap<Entity, int>(8, Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
        {
            var center = orbits[i].Center;
            if (!horizons.HasComponent(center)) { released[i] = true; continue; }
            counts.TryGetValue(center, out int count);
            counts[center] = count + 1;
        }

        foreach (var pair in counts)
        {
            int excess = pair.Value - math.max(1, horizons[pair.Key].MaxProjectiles);
            for (; excess > 0; excess--)
            {
                int oldest = -1;
                for (int i = 0; i < entities.Length; i++)
                    if (!released[i] && orbits[i].Center == pair.Key && (oldest < 0 || orbits[i].Order < orbits[oldest].Order)) oldest = i;
                if (oldest < 0) break;
                released[oldest] = true;
            }
        }

        var release = new NativeList<Entity>(Allocator.Temp);
        for (int i = 0; i < entities.Length; i++)
            if (released[i]) release.Add(entities[i]);
        if (release.Length > 0) state.EntityManager.RemoveComponent<Orbiting>(release.AsArray());
    }
}

/// <summary>
/// Steers orbiting projectiles around their center: toward the circle's tangent (all orbits turn the same way) plus a
/// pull toward their radius (from their current speed, every frame), turned at most TurnSpeed per second and keeping their speed. Each
/// wearer's projectiles spread evenly around the circle: one out of its place orbits a little further out or in until
/// it's back (SpreadStrength, SpreadError). A new projectile spirals out
/// from the wearer. After homing and wandering, which keep steering too (they work against each other), and before
/// ProjectileSystem moves them.
/// </summary>
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateAfter(typeof(HomingProjectileSystem))]
[UpdateAfter(typeof(WanderingProjectileSystem))]
[UpdateBefore(typeof(ProjectileSystem))]
[BurstCompile]
public partial struct OrbitSteeringSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // The lookups are read here on the main thread: UnitMoverSystem's job may still be writing LocalTransform
        state.CompleteDependency();
        var transforms = SystemAPI.GetComponentLookup<LocalTransform>(true);
        var horizons = SystemAPI.GetComponentLookup<EventHorizon>(true);
        float deltaTime = SystemAPI.Time.DeltaTime;

        // Every orbiting projectile, to find each one's place among its wearer's (oldest first) for the spreading
        var all = SystemAPI.QueryBuilder().WithAll<Orbiting, Projectile>().Build();
        var allEntities = all.ToEntityArray(Allocator.Temp);
        var allOrbits = all.ToComponentDataArray<Orbiting>(Allocator.Temp);

        foreach (var (projectile, orbit, entity) in SystemAPI.Query<RefRW<Projectile>, RefRO<Orbiting>>().WithEntityAccess())
        {
            var center = orbit.ValueRO.Center;
            // Released by the capture system next frame
            if (!transforms.HasComponent(center) || !horizons.HasComponent(center) || !transforms.HasComponent(entity)) continue;
            var horizon = horizons[center];

            var offset = transforms[entity].Position - transforms[center].Position;
            offset.y = 0f;
            float distance = math.length(offset);
            var direction = projectile.ValueRO.Direction;
            var flatDirection = math.normalizesafe(new float3(direction.x, 0f, direction.z), new float3(1f, 0f, 0f));
            var radial = distance > 1e-3f ? offset / distance : flatDirection;
            var tangent = new float3(-radial.z, 0f, radial.x);

            // Faster projectiles orbit further out, by their speed right now (slows included), so one that speeds up drifts outward
            float radius = math.max(math.clamp(projectile.ValueRO.CurrentSpeed * horizon.RadiusPerSpeed, horizon.MinRadius, horizon.MaxRadius), 0.1f);
            radius *= 1f + horizon.SpreadStrength * SpreadError(entity, orbit.ValueRO, allEntities, allOrbits, transforms, transforms[center].Position) / math.PI;
            float error = (radius - distance) / radius;
            var desired = math.normalizesafe(tangent + radial * math.clamp(error * horizon.PullStrength, -1f, 1f), tangent);

            float speed = math.length(direction);
            projectile.ValueRW.Direction = Vector3.RotateTowards(direction, desired * speed, horizon.TurnSpeed * deltaTime, 0f);
        }
    }

    /// <summary>
    /// How far ahead (positive, radians) a projectile is of its even-spaced place in its wearer's orbit: a full turn / their count apart, in
    /// the order they already are around the circle (counted back from the oldest), centered on where they are now. Radius changes from this make
    /// one ahead orbit further out (slower around) and one behind further in (faster around), as the speed stays the same.
    /// </summary>
    static float SpreadError(Entity entity, in Orbiting orbit, NativeArray<Entity> entities, NativeArray<Orbiting> orbits,
                             in ComponentLookup<LocalTransform> transforms, float3 center)
    {
        int oldest = -1;
        for (int i = 0; i < entities.Length; i++)
            if (orbits[i].Center == orbit.Center && transforms.HasComponent(entities[i]) && (oldest < 0 || orbits[i].Order < orbits[oldest].Order)) oldest = i;
        if (oldest < 0) return 0f;

        // Its place in line is how many are between it and the oldest, going back around the circle, so none pass each other
        float anchor = AngleAround(transforms[entities[oldest]].Position, center);
        float behind = Behind(anchor, AngleAround(transforms[entity].Position, center));
        int rank = 0, count = 0;
        float sumBehind = 0f;
        for (int i = 0; i < entities.Length; i++)
        {
            if (orbits[i].Center != orbit.Center || !transforms.HasComponent(entities[i])) continue;
            count++;
            float other = Behind(anchor, AngleAround(transforms[entities[i]].Position, center));
            sumBehind += other;
            if (other < behind || (other == behind && orbits[i].Order < orbit.Order)) rank++;
        }

        // Ahead of its place: it's less far behind the oldest than it should be. The places are shifted so the errors
        // average out (the places' average is π(count - 1)/count behind), so half move forward and half back.
        float spacing = 2f * math.PI / count;
        float shift = math.PI * (count - 1) / count - sumBehind / count;
        return rank * spacing - behind - shift;
    }

    // How far angle is behind anchor, going back around the circle: 0 to 2π
    static float Behind(float anchor, float angle)
    {
        float behind = anchor - angle;
        return behind - 2f * math.PI * math.floor(behind / (2f * math.PI));
    }

    // Angle around center, increasing the way the orbits turn
    static float AngleAround(float3 position, float3 center) => math.atan2(position.z - center.z, position.x - center.x);
}
