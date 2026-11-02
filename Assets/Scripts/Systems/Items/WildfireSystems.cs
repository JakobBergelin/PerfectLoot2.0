using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>Wildfire legendary power, added by WildfirePower while the item is equipped.</summary>
public struct Wildfire : IComponentData
{
    public float SpreadFraction;    // Of the dying enemy's remaining burn damage, given to each enemy around it
    public float Radius;            // Scaled by the wearer's area of effect
}

/// <summary>
/// Wildfire: when a burning enemy dies, the burn damage it had left from a wearer's burns is given to every enemy within
/// Radius of it (times SpreadFraction) as one new burn over the usual burn duration, by that wearer. Those can spread it
/// again when they die. After the damage is dealt and before CheckDeathSystem removes the dead, while they still burn.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup), OrderLast = true)]
[UpdateAfter(typeof(DamagingSystem))]
[UpdateBefore(typeof(CheckDeathSystem))]
[BurstCompile]
public partial struct WildfireSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<StatusEffectConfig>();
        state.RequireForUpdate<Wildfire>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var settings = SystemAPI.GetSingleton<StatusEffectConfig>();
        if (settings.BurnDuration <= 0f) return;

        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        var hits = new NativeList<DistanceHit>(Allocator.Temp);
        var filter = new CollisionFilter
        {
            BelongsTo = ~0u,
            CollidesWith = (uint)CollisionLayerHelper.AnyEnemy,
            GroupIndex = 0,
        };
        var wildfires = SystemAPI.GetComponentLookup<Wildfire>(true);
        var stats = SystemAPI.GetComponentLookup<CalculatedStats>(true);
        var health = SystemAPI.GetComponentLookup<Health>(true);
        var burns = SystemAPI.GetBufferLookup<BurnInstance>();
        var burning = SystemAPI.GetComponentLookup<Burning>();

        // Collected first: spreading adds burns to the buffers being read
        var spreads = new NativeList<Spread>(Allocator.Temp);
        foreach (var (dyingHealth, transform, entity) in SystemAPI.Query<RefRO<Health>, RefRO<LocalTransform>>()
                     .WithAll<Enemy, Burning>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            if (dyingHealth.ValueRO.CurrentValue > 0f) continue;

            // Usually one wearer; each wearer spreads its own burns
            var remaining = new FixedList128Bytes<Spread>();
            foreach (var burn in burns[entity])
            {
                if (!wildfires.HasComponent(burn.Source)) continue;
                float damage = burn.DamagePerSecond * math.max(0f, burn.TimeRemaining);
                int index = -1;
                for (int i = 0; i < remaining.Length; i++)
                    if (remaining[i].Source == burn.Source) index = i;
                if (index >= 0) remaining.ElementAt(index).Damage += damage;
                else if (remaining.Length < remaining.Capacity)
                    remaining.Add(new Spread { Origin = entity, Position = transform.ValueRO.Position, Source = burn.Source, Damage = damage });
            }
            foreach (var spread in remaining) spreads.Add(spread);
        }

        foreach (var spread in spreads)
        {
            var wildfire = wildfires[spread.Source];
            float damage = spread.Damage * wildfire.SpreadFraction;
            if (damage <= 0f) continue;
            float area = stats.HasComponent(spread.Source) ? stats[spread.Source].Value.AreaMultiplier : 1f;

            hits.Clear();
            physicsWorld.OverlapSphere(spread.Position, wildfire.Radius * area, ref hits, filter);
            foreach (var hit in hits)
            {
                // Not the dying enemy, nor others dying this frame (they spread their own)
                if (hit.Entity == spread.Origin || !health.HasComponent(hit.Entity) || health[hit.Entity].CurrentValue <= 0f) continue;
                EffectApply.AddBurn(burns, burning, hit.Entity, damage / settings.BurnDuration, settings.BurnDuration, spread.Source);
            }
        }
    }

    struct Spread
    {
        public Entity Origin;
        public float3 Position;
        public Entity Source;
        public float Damage;
    }
}
