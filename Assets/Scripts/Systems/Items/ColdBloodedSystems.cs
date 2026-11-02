using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>Cold Blooded legendary power, added by ColdBloodedPower while the item is equipped.</summary>
public struct ColdBlooded : IComponentData
{
    public float DamageMultiplier;  // Cold damage per second for each point of health regeneration
    public float Radius;            // Scaled by the wearer's area of effect
    public float TickRate;          // Seconds between hits

    public float Timer;             // Until the next hit
}

/// <summary>
/// Cold Blooded: the wearer's health regeneration doesn't heal it (HealthRegenerationSystem skips it); every TickRate
/// seconds enemies within Radius take that regeneration as cold damage instead. A normal main hit by the wearer:
/// damage %, element conversion, crits and chill apply. Nothing while regeneration is 0 or negative.
/// </summary>
[UpdateInGroup(typeof(AttackSystemGroup))]
[BurstCompile]
public partial struct ColdBloodedSystem : ISystem
{
    Random _random;

    public void OnCreate(ref SystemState state)
    {
        _random = Random.CreateFromIndex((uint)System.Environment.TickCount);
        state.RequireForUpdate<PhysicsWorldSingleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        var hits = new NativeList<DistanceHit>(Allocator.Temp);
        var filter = new CollisionFilter
        {
            BelongsTo = ~0u,
            CollidesWith = (uint)CollisionLayerHelper.AnyEnemy,
            GroupIndex = 0,
        };
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (coldBlooded, regeneration, stats, transform, entity) in
                 SystemAPI.Query<RefRW<ColdBlooded>, RefRO<HealthRegeneration>, RefRO<CalculatedStats>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            coldBlooded.ValueRW.Timer -= deltaTime;
            if (coldBlooded.ValueRO.Timer > 0f) continue;
            float tickRate = math.max(coldBlooded.ValueRO.TickRate, 0.05f);
            coldBlooded.ValueRW.Timer += tickRate;
            if (coldBlooded.ValueRO.Timer <= 0f) coldBlooded.ValueRW.Timer = tickRate;

            float perSecond = regeneration.ValueRO.Value * coldBlooded.ValueRO.DamageMultiplier;
            if (perSecond <= 0f) continue;

            var type = stats.ValueRO.Value.ConvertedType(DamageType.Cold);
            float baseDamage = perSecond * tickRate * stats.ValueRO.Value.DamageMultiplier(type);
            var position = transform.ValueRO.Position;

            hits.Clear();
            physicsWorld.OverlapSphere(position, coldBlooded.ValueRO.Radius * stats.ValueRO.Value.AreaMultiplier, ref hits, filter);
            foreach (var hit in hits)
            {
                if (hit.Entity == entity || !SystemAPI.HasComponent<Health>(hit.Entity) || !SystemAPI.HasBuffer<DamageInstance>(hit.Entity)) continue;

                float damage = baseDamage;
                bool isCrit = false;
                AttackSystem.CalculateCrit(entity, hit.Entity, state.EntityManager, ref _random, ref damage, ref isCrit);
                var targetPosition = SystemAPI.GetComponent<LocalTransform>(hit.Entity).Position;
                SystemAPI.GetBuffer<DamageInstance>(hit.Entity).Add(new DamageInstance
                {
                    Damage = damage, Type = type, Hierarchy = DamageHierarchy.Main, Source = entity, IsCrit = isCrit,
                    HitDirection = AttackSystem.HitDirection(position, targetPosition, ref _random),
                });
            }
        }
    }
}
