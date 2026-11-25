using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>Seeking legendary power, added by SeekingPower while the item is equipped.</summary>
public struct Seeking : IComponentData
{
    public float CursorRadius;      // Meters around the cursor to look for the first target
    public float TurnSpeed;         // Radians per second at the start
    public float TurnAcceleration;  // Radians per second, per second
}

/// <summary>
/// Seeking: makes each projectile a wearer fires homing (HomingProjectile) on the frame it spawns (still marked
/// InitializeFromSource), locked on the living enemy nearest the wearer's cursor within CursorRadius; without one there,
/// HomingProjectileSystem picks the enemy nearest the projectile. Not projectiles that steer themselves (already homing,
/// wandering, Captain's Shield, Cursed Hammer). With Event Horizon too, homing and orbiting both steer
/// (OrbitSteeringSystem runs after homing), so the projectile pulls between its orbit and its target.
/// </summary>
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateBefore(typeof(HomingProjectileSystem))]
[BurstCompile]
public partial struct SeekingSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Seeking>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var world = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        var filter = new CollisionFilter { BelongsTo = ~0u, CollidesWith = (uint)CollisionLayerHelper.AnyEnemy, GroupIndex = 0 };
        var hits = new NativeList<DistanceHit>(Allocator.Temp);
        var newHoming = new NativeList<Entity>(Allocator.Temp);
        var targets = new NativeList<Entity>(Allocator.Temp);
        var turnSpeeds = new NativeList<float2>(Allocator.Temp);

        foreach (var (source, target, entity) in SystemAPI.Query<RefRO<Source>, RefRO<AttackTarget>>()
                     .WithAll<Projectile, Attack>().WithPresent<InitializeFromSource>()
                     .WithNone<HomingProjectile, WanderingProjectile, Orbiting>().WithNone<ShieldProjectile, CursedHammerProjectile>().WithEntityAccess())
        {
            var wearer = source.ValueRO.Entity;
            if (target.ValueRO.TargetType != TargetType.Enemy || !SystemAPI.HasComponent<Seeking>(wearer)) continue;
            var seeking = SystemAPI.GetComponent<Seeking>(wearer);
            var cursor = SystemAPI.HasComponent<InputsData>(wearer) ? SystemAPI.GetComponent<InputsData>(wearer).MousePosition : float3.zero;
            newHoming.Add(entity);
            targets.Add(SystemAPI.HasComponent<InputsData>(wearer) ? NearestTo(ref state, world, hits, filter, cursor, seeking.CursorRadius) : Entity.Null);
            turnSpeeds.Add(new float2(seeking.TurnSpeed, seeking.TurnAcceleration));
        }

        for (int i = 0; i < newHoming.Length; i++)
            state.EntityManager.AddComponentData(newHoming[i], new HomingProjectile
            {
                Target = targets[i], TurnSpeed = turnSpeeds[i].x, TurnAcceleration = turnSpeeds[i].y, HomingType = HomingType.Closest,
            });
    }

    // The living enemy nearest this spot within radius, or Entity.Null
    Entity NearestTo(ref SystemState state, in CollisionWorld world, NativeList<DistanceHit> hits, CollisionFilter filter, float3 spot, float radius)
    {
        hits.Clear();
        world.OverlapSphere(spot, radius, ref hits, filter);
        var best = Entity.Null;
        float bestDistance = float.MaxValue;
        foreach (var hit in hits)
        {
            if (!SystemAPI.HasComponent<Enemy>(hit.Entity) || !SystemAPI.HasComponent<Health>(hit.Entity) || !SystemAPI.HasComponent<LocalTransform>(hit.Entity)) continue;
            if (SystemAPI.GetComponent<Health>(hit.Entity).CurrentValue <= 0f) continue;
            float distance = math.distancesq(SystemAPI.GetComponent<LocalTransform>(hit.Entity).Position.xz, spot.xz);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = hit.Entity;
        }
        return best;
    }
}
