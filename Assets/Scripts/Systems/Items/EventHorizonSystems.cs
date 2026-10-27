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
        foreach (var (source, projectile, entity) in SystemAPI.Query<RefRO<Source>, RefRO<Projectile>>()
                     .WithPresent<InitializeFromSource>().WithNone<Orbiting>().WithEntityAccess())
        {
            var wearer = source.ValueRO.Entity;
            if (!horizons.HasComponent(wearer)) continue;
            var horizon = horizons.GetRefRW(wearer);
            float radius = math.clamp(projectile.ValueRO.Speed * horizon.ValueRO.RadiusPerSpeed, horizon.ValueRO.MinRadius, horizon.ValueRO.MaxRadius);
            commandBuffer.AddComponent(entity, new Orbiting { Center = wearer, Radius = math.max(radius, 0.1f), Order = horizon.ValueRO.NextOrder });
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
/// pull toward their radius, turned at most TurnSpeed per second and keeping their speed. A new projectile spirals out
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

            float error = (orbit.ValueRO.Radius - distance) / orbit.ValueRO.Radius;
            var desired = math.normalizesafe(tangent + radial * math.clamp(error * horizon.PullStrength, -1f, 1f), tangent);

            float speed = math.length(direction);
            projectile.ValueRW.Direction = Vector3.RotateTowards(direction, desired * speed, horizon.TurnSpeed * deltaTime, 0f);
        }
    }
}
