using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Quarry: all the wearer's damage to enemies other than its target is lowered by DamagePenalty, damage over time
/// included. With no target, nothing is. Between working out the damage and dealing it.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateBefore(typeof(DamagingSystem))]
[BurstCompile]
public partial struct QuarryDamageSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Quarry>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var quarries = SystemAPI.GetComponentLookup<Quarry>(true);

        foreach (var (taken, entity) in SystemAPI.Query<DynamicBuffer<CalculatedDamageInstance>>().WithAll<Enemy>().WithEntityAccess())
        {
            for (int i = 0; i < taken.Length; i++)
            {
                ref var instance = ref taken.ElementAt(i);
                if (instance.Damage <= 0f || !quarries.HasComponent(instance.Source)) continue;

                var quarry = quarries[instance.Source];
                if (quarry.Target != Entity.Null && quarry.Target != entity) instance.Damage *= 1f - quarry.DamagePenalty;
            }
        }
    }
}

/// <summary>
/// Quarry: when the wearer's target dies, whatever killed it, the wearer gets ResourceRefund of its maximum resource back.
/// A target that died or is further away than Radius is replaced by a random living enemy within Radius. After all of
/// this frame's damage and before CheckDeathSystem removes the dead, so each death is counted once.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup), OrderLast = true)]
[UpdateBefore(typeof(CheckDeathSystem))]
[BurstCompile]
public partial struct QuarrySystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Quarry>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var healths = SystemAPI.GetComponentLookup<Health>(true);
        var transforms = SystemAPI.GetComponentLookup<LocalTransform>(true);
        state.CompleteDependency();  // The lookups are used here on the main thread

        foreach (var (quarry, resource, resourceChanges, transform) in SystemAPI.Query<RefRW<Quarry>, RefRO<Resource>, DynamicBuffer<ResourceChanger>, RefRO<LocalTransform>>())
        {
            ref var q = ref quarry.ValueRW;
            if (healths.HasComponent(q.Target) && healths[q.Target].CurrentValue <= 0f)
            {
                resourceChanges.Add(new ResourceChanger { Value = resource.ValueRO.MaxValue * q.ResourceRefund });
                q.Target = Entity.Null;
            }

            var position = transform.ValueRO.Position;
            float radiusSq = q.Radius * q.Radius;
            if (!transforms.HasComponent(q.Target) || math.distancesq(transforms[q.Target].Position.xz, position.xz) > radiusSq)
                q.Target = PickTarget(ref state, ref q.Random, position, radiusSq);
        }
    }

    // Picks evenly among the living enemies in range
    Entity PickTarget(ref SystemState state, ref Random random, float3 position, float radiusSq)
    {
        var picked = Entity.Null;
        int candidates = 0;
        foreach (var (health, transform, enemy) in SystemAPI.Query<RefRO<Health>, RefRO<LocalTransform>>()
                     .WithAll<Enemy>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            if (health.ValueRO.CurrentValue <= 0f || math.distancesq(transform.ValueRO.Position.xz, position.xz) > radiusSq) continue;
            candidates++;
            if (random.NextInt(candidates) == 0) picked = enemy;
        }
        return picked;
    }
}
