using Unity.Burst;
using Unity.Entities;
using Unity.Physics;
using Unity.Collections;
using Unity.Transforms;

/// <summary>Instant heal on every unit of Type within Radius, on the frame the entity exists (e.g. Luminosity).</summary>
public struct Heal : IComponentData
{
    public float Value;
    public float Radius;
    public TargetType Type;
}

/// <summary>
/// Heal-over-time area: heals units of Type within Radius by HealPerSecond, paid out every TickRate seconds, until
/// TimeRemaining runs out. Put it on an entity with a LocalTransform and a Source. Overlapping areas stack.
/// </summary>
public struct PeriodicHeal : IComponentData
{
    public float HealPerSecond;
    public float Radius;
    public float TimeRemaining;
    public float TickRate;
    public TargetType Type;
}

/// <summary>On every unit: healing gathered from one PeriodicHeal area, paid out as a HealingInstance when Tick runs out.</summary>
public struct PeriodicHealingInstance : IBufferElementData
{
    public Entity Area;
    public float AccumulatedHeal;
    public float Tick;
    public Entity Source;
}

public static class HealingUtil
{
    public static CollisionFilter UnitFilter => new CollisionFilter
    {
        BelongsTo = ~0u,
        CollidesWith = (uint)CollisionLayerHelper.Unit,
        GroupIndex = 0,
    };

    public static bool IsTarget(TargetType type, bool isPlayer, bool isEnemy) => type switch
    {
        TargetType.Player => isPlayer,
        TargetType.Enemy => isEnemy,
        _ => isPlayer || isEnemy,
    };
}

[BurstCompile]
[UpdateInGroup(typeof(HealthAndResourceSystemGroup))]
[UpdateBefore(typeof(ApplyHealingSystem))]
public partial class HealingSystem : SystemBase
{
    [BurstCompile]
    protected override void OnUpdate()
    {
        var physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        var outHits = new NativeList<DistanceHit>(Allocator.Temp);
        var collisionFilter = HealingUtil.UnitFilter;

        foreach (var (heal, localTransform, source, entity) in SystemAPI.Query<RefRO<Heal>, RefRO<LocalTransform>, RefRO<Source>>().WithEntityAccess())
        {
            outHits.Clear();
            physicsWorldSingleton.OverlapSphere(localTransform.ValueRO.Position, heal.ValueRO.Radius, ref outHits, collisionFilter);
            foreach (var hit in outHits)
            {
                if (hit.Entity == entity) continue;
                if (!HealingUtil.IsTarget(heal.ValueRO.Type, SystemAPI.HasComponent<Player>(hit.Entity), SystemAPI.HasComponent<Enemy>(hit.Entity))) continue;
                if (!SystemAPI.HasBuffer<HealingInstance>(hit.Entity)) continue;

                // Written directly (not via ECB) so ApplyHealingSystem handles it this frame
                SystemAPI.GetBuffer<HealingInstance>(hit.Entity).Add(new HealingInstance { Value = heal.ValueRO.Value, Source = source.ValueRO.Entity });
            }
        }
    }
}

/// <summary>Gathers healing from PeriodicHeal areas into the units inside them, and removes areas that ran out.</summary>
[BurstCompile]
[UpdateInGroup(typeof(HealthAndResourceSystemGroup))]
[UpdateBefore(typeof(AccumilatedPeriodicHealSystem))]
public partial class PeriodicHealSystem : SystemBase
{
    [BurstCompile]
    protected override void OnUpdate()
    {
        var physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        var outHits = new NativeList<DistanceHit>(Allocator.Temp);
        var collisionFilter = HealingUtil.UnitFilter;
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (periodicHeal, localTransform, source, entity) in SystemAPI.Query<RefRO<PeriodicHeal>, RefRO<LocalTransform>, RefRO<Source>>().WithEntityAccess())
        {
            outHits.Clear();
            physicsWorldSingleton.OverlapSphere(localTransform.ValueRO.Position, periodicHeal.ValueRO.Radius, ref outHits, collisionFilter);
            float heal = periodicHeal.ValueRO.HealPerSecond * deltaTime;

            foreach (var hit in outHits)
            {
                if (hit.Entity == entity) continue;
                if (!HealingUtil.IsTarget(periodicHeal.ValueRO.Type, SystemAPI.HasComponent<Player>(hit.Entity), SystemAPI.HasComponent<Enemy>(hit.Entity))) continue;
                if (!SystemAPI.HasBuffer<PeriodicHealingInstance>(hit.Entity)) continue;

                // One instance per area, so overlapping areas stack
                var instances = SystemAPI.GetBuffer<PeriodicHealingInstance>(hit.Entity);
                int index = -1;
                for (int i = 0; i < instances.Length; i++)
                {
                    if (instances[i].Area == entity) { index = i; break; }
                }
                if (index == -1)
                {
                    instances.Add(new PeriodicHealingInstance { Area = entity, AccumulatedHeal = heal, Tick = periodicHeal.ValueRO.TickRate, Source = source.ValueRO.Entity });
                }
                else
                {
                    instances.ElementAt(index).AccumulatedHeal += heal;
                }
            }
        }

        // Areas that ran out are removed at the end of the frame
        var toDestroy = new NativeList<Entity>(Allocator.Temp);
        foreach (var (periodicHeal, entity) in SystemAPI.Query<RefRW<PeriodicHeal>>().WithEntityAccess())
        {
            periodicHeal.ValueRW.TimeRemaining -= deltaTime;
            if (periodicHeal.ValueRO.TimeRemaining <= 0.0f) toDestroy.Add(entity);
        }
        if (!toDestroy.IsEmpty)
        {
            var commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);
            commandBuffer.AddComponent<DestroyWithChildren>(toDestroy.AsArray());
        }
    }
}

/// <summary>Pays out gathered periodic healing as a HealingInstance every tick (ApplyHealingSystem caps it at max health).</summary>
[BurstCompile]
[UpdateInGroup(typeof(HealthAndResourceSystemGroup))]
[UpdateBefore(typeof(ApplyHealingSystem))]
public partial class AccumilatedPeriodicHealSystem : SystemBase
{
    [BurstCompile]
    protected override void OnUpdate()
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        foreach (var (periodicInstances, healingInstances) in SystemAPI.Query<DynamicBuffer<PeriodicHealingInstance>, DynamicBuffer<HealingInstance>>())
        {
            for (int i = 0; i < periodicInstances.Length; i++)
            {
                ref var instance = ref periodicInstances.ElementAt(i);
                instance.Tick -= deltaTime;
                if (instance.Tick > 0.0f) continue;

                if (instance.AccumulatedHeal > 0.0f)
                    healingInstances.Add(new HealingInstance { Value = instance.AccumulatedHeal, Source = instance.Source });
                periodicInstances.RemoveAt(i);
                i--;
            }
        }
    }
}
