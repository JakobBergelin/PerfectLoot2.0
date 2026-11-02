using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>Toxic Trail legendary power, added by ToxicTrailPower while the item is equipped.</summary>
public struct ToxicTrail : IComponentData
{
    public float DamagePerSecond;   // Poison, before the wearer's damage %
    public float Lifetime;          // Seconds each hazard lingers
    public float Radius;            // Scaled by the wearer's area of effect
    public float Spacing;           // Meters moved between hazards

    public float3 LastHazard;       // Where the last hazard was left (or where the wearer stood when equipped)
    public bool Started;
}

/// <summary>
/// Toxic Trail: each time the wearer has moved Spacing from the last one, leaves a poison Hazard where it stands
/// (HazardSystem deals the damage). Damage %, element conversion and area of effect are taken when it is left.
/// </summary>
[UpdateInGroup(typeof(AttackSystemGroup))]
[UpdateBefore(typeof(HazardSystem))]
[BurstCompile]
public partial struct ToxicTrailSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<ToxicTrail>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Created after the loop: no structural changes while iterating
        var hazards = new NativeList<Spawn>(Allocator.Temp);
        foreach (var (trail, transform, stats, entity) in SystemAPI.Query<RefRW<ToxicTrail>, RefRO<LocalTransform>, RefRO<CalculatedStats>>().WithEntityAccess())
        {
            ref var t = ref trail.ValueRW;
            var position = transform.ValueRO.Position;
            // Not one at the feet the moment the boots are put on
            if (!t.Started)
            {
                t.Started = true;
                t.LastHazard = position;
                continue;
            }
            if (math.distance(position, t.LastHazard) < math.max(0.1f, t.Spacing)) continue;

            t.LastHazard = position;
            var s = stats.ValueRO.Value;
            var type = s.ConvertedType(DamageType.Poison);
            hazards.Add(new Spawn
            {
                Source = entity, Position = position,
                Hazard = new Hazard { Type = type, DamagePerSecond = t.DamagePerSecond * s.DamageMultiplier(type), Radius = t.Radius * s.AreaMultiplier, TimeRemaining = t.Lifetime },
            });
        }

        foreach (var spawn in hazards)
        {
            var hazard = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(hazard, LocalTransform.FromPosition(spawn.Position));
            state.EntityManager.AddComponentData(hazard, spawn.Hazard);
            state.EntityManager.AddComponentData(hazard, new Source { Entity = spawn.Source });
        }
    }

    struct Spawn
    {
        public Entity Source;
        public float3 Position;
        public Hazard Hazard;
    }
}
