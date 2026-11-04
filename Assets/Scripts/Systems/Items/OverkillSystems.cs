using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>Overkill legendary power, added by OverkillPower while the item is equipped.</summary>
public struct Overkill : IComponentData
{
    public float Radius;    // Scaled by the wearer's area of effect
}

/// <summary>
/// Overkill: when a main hit of the wearer kills an enemy, the damage it had to spare becomes a new hit of the same
/// element on the nearest living enemy within Radius, next frame. That hit can kill and carry over again. Damage over
/// time doesn't count. After the damage is dealt and before CheckDeathSystem removes the dead.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup), OrderLast = true)]
[UpdateAfter(typeof(DamagingSystem))]
[UpdateBefore(typeof(CheckDeathSystem))]
[BurstCompile]
public partial struct OverkillSystem : ISystem
{
    uint _frame;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<Overkill>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var random = Unity.Mathematics.Random.CreateFromIndex(_frame++);
        double now = SystemAPI.Time.ElapsedTime;
        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        var overkills = SystemAPI.GetComponentLookup<Overkill>(true);
        var statsLookup = SystemAPI.GetComponentLookup<CalculatedStats>(true);
        var healthLookup = SystemAPI.GetComponentLookup<Health>(true);
        var transforms = SystemAPI.GetComponentLookup<LocalTransform>(true);
        var damageBuffers = SystemAPI.GetBufferLookup<DamageInstance>();

        var hits = new NativeList<DistanceHit>(Allocator.Temp);
        var filter = new CollisionFilter
        {
            BelongsTo = ~0u,
            CollidesWith = (uint)CollisionLayerHelper.AnyEnemy,
            GroupIndex = 0,
        };

        foreach (var (health, lastHit, transform, entity) in SystemAPI.Query<RefRO<Health>, RefRO<LastHit>, RefRO<LocalTransform>>()
                     .WithAll<Enemy>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            float spare = -health.ValueRO.CurrentValue;
            var source = lastHit.ValueRO.Source;
            if (spare <= 0f || lastHit.ValueRO.Time != now || !overkills.HasComponent(source)) continue;

            var position = transform.ValueRO.Position;
            float area = statsLookup.HasComponent(source) ? statsLookup[source].Value.AreaMultiplier : 1f;
            hits.Clear();
            physicsWorld.OverlapSphere(position, overkills[source].Radius * area, ref hits, filter);

            var nearest = Entity.Null;
            float nearestDistanceSq = float.MaxValue;
            foreach (var hit in hits)
            {
                if (hit.Entity == entity || !healthLookup.HasComponent(hit.Entity) || healthLookup[hit.Entity].CurrentValue <= 0f || !damageBuffers.HasBuffer(hit.Entity)) continue;
                float distanceSq = math.distancesq(position, transforms[hit.Entity].Position);
                if (distanceSq < nearestDistanceSq)
                {
                    nearestDistanceSq = distanceSq;
                    nearest = hit.Entity;
                }
            }
            if (nearest == Entity.Null) continue;

            damageBuffers[nearest].Add(new DamageInstance
            {
                Damage = spare, Type = lastHit.ValueRO.Type, Hierarchy = DamageHierarchy.Main, Source = source, IsCenterTarget = true,
                HitDirection = AttackSystem.HitDirection(position, transforms[nearest].Position, ref random),
            });
        }
    }
}
