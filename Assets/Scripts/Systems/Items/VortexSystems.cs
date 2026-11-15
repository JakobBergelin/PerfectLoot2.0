using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>Vortex unique power, added by VortexPower while the item is equipped.</summary>
public struct Vortex : IComponentData
{
    public float PullRadius;    // Meters around the wearer that enemies are pulled from, times the Area multiplier
    public float PullSpeed;     // Meters per second toward the wearer
    public float StopDistance;  // Enemies this close aren't pulled further
}

/// <summary>
/// Vortex: while the wearer spins with Whirlwind, enemies within PullRadius (grown by Area of Effect) are pulled toward it
/// until they're within StopDistance. The pull is set as their knockback, so walls, water and edges stop it like a push.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
[UpdateAfter(typeof(WhirlwindSystem))]
public partial struct VortexSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Vortex>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // The spinning wearers: where they are and how far they pull
        var centers = new NativeList<float3>(Allocator.Temp);
        var vortexes = new NativeList<Vortex>(Allocator.Temp);
        var radii = new NativeList<float>(Allocator.Temp);
        foreach (var (vortex, whirlwind, stats, transform) in SystemAPI.Query<RefRO<Vortex>, RefRO<Whirlwind>, RefRO<CalculatedStats>, RefRO<LocalTransform>>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (!whirlwind.ValueRO.Spinning) continue;
            centers.Add(transform.ValueRO.Position);
            vortexes.Add(vortex.ValueRO);
            radii.Add(vortex.ValueRO.PullRadius * stats.ValueRO.Value.AreaMultiplier);
        }
        if (centers.Length == 0) return;

        // Each enemy is pulled toward the nearest spinning wearer that reaches it, at least at PullSpeed
        foreach (var (knockback, transform) in SystemAPI.Query<RefRW<Knockback>, RefRO<LocalTransform>>().WithAll<Enemy>())
        {
            int nearest = -1;
            float nearestDistance = float.MaxValue;
            for (int i = 0; i < centers.Length; i++)
            {
                float distance = math.distance(centers[i].xz, transform.ValueRO.Position.xz);
                if (distance <= radii[i] && distance < nearestDistance)
                {
                    nearest = i;
                    nearestDistance = distance;
                }
            }
            if (nearest < 0 || nearestDistance <= vortexes[nearest].StopDistance) continue;

            var toCenter = centers[nearest] - transform.ValueRO.Position;
            toCenter.y = 0f;
            var inward = toCenter / nearestDistance;
            var velocity = knockback.ValueRO.Velocity;
            float inwardSpeed = math.dot(velocity, inward);
            if (inwardSpeed < vortexes[nearest].PullSpeed)
                knockback.ValueRW.Velocity = velocity + inward * (vortexes[nearest].PullSpeed - inwardSpeed);
        }
    }
}
