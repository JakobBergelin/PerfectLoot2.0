using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>Poison Trail passive: the hazards an enemy leaves behind as it moves.</summary>
public struct PoisonTrail : IComponentData
{
    public DamageType Type;
    public float DamagePerSecond;
    public float Radius;
    public float Lifetime;
    public float Spacing;       // Meters moved between two hazards
    public float3 LastDrop;
    public bool HasDropped;
}

/// <summary>
/// A living enemy with Poison Trail leaves a Hazard that hurts players and companions (HitsPlayers) where it stands, and
/// another every Spacing meters it moves, so standing still doesn't pile them up. All its hazards share it as their
/// Source, so overlapping ones count once (HazardSystem). None while it's in the air (Leap).
/// </summary>
[UpdateInGroup(typeof(SkillSystemGroup))]
[BurstCompile]
public partial struct PoisonTrailSystem : ISystem
{
    const float RootHeight = 1f;    // Hazard art is drawn for a root this far above the floor (a unit of scale 1)

    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<PoisonTrail>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var drops = new NativeList<Hazard>(Allocator.Temp);
        var positions = new NativeList<float3>(Allocator.Temp);
        var sources = new NativeList<Entity>(Allocator.Temp);
        var leaps = SystemAPI.GetComponentLookup<EnemyLeap>(true);
        foreach (var (trail, health, transform, collider, entity) in SystemAPI.Query<RefRW<PoisonTrail>, RefRO<Health>, RefRO<LocalTransform>, RefRO<PhysicsCollider>>()
                     .WithNone<SpawnRise>().WithEntityAccess())
        {
            if (health.ValueRO.CurrentValue <= 0f) continue;
            if (leaps.HasComponent(entity) && leaps[entity].Airborne) continue;   // Nothing while in the air
            ref var t = ref trail.ValueRW;
            // From the floor under it, not its own middle, which is higher up on a big enemy
            var position = transform.ValueRO.Position;
            position.y += RootHeight - UnitMovement.BottomOffset(collider.ValueRO, transform.ValueRO.Rotation, transform.ValueRO.Scale);
            if (t.HasDropped && math.distance(position.xz, t.LastDrop.xz) < t.Spacing) continue;
            t.HasDropped = true;
            t.LastDrop = position;
            drops.Add(new Hazard { Type = t.Type, DamagePerSecond = t.DamagePerSecond, Radius = t.Radius, TimeRemaining = t.Lifetime, HitsPlayers = true });
            positions.Add(position);
            sources.Add(entity);
        }

        for (int i = 0; i < drops.Length; i++)
        {
            var hazard = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(hazard, LocalTransform.FromPosition(positions[i]));
            state.EntityManager.AddComponentData(hazard, drops[i]);
            state.EntityManager.AddComponentData(hazard, new Source { Entity = sources[i] });
        }
    }
}
