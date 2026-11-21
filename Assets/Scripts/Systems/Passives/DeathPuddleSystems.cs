using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

/// <summary>Death Puddle passive: the hazard an enemy leaves where it dies.</summary>
public struct DeathPuddle : IComponentData
{
    public DamageType Type;
    public float DamagePerSecond;
    public float Radius;
    public float Lifetime;
}

/// <summary>A Death Puddle enemy whose puddle was already left (dying enemies can stay at 0 health for a while).</summary>
public struct DeathPuddleLeft : IComponentData {}

/// <summary>
/// An enemy with Death Puddle that reached 0 health leaves a Hazard where it stood that hurts players and companions
/// (HitsPlayers; HazardSystem deals the damage, with its type's status effect). Before CheckDeathSystem removes the dead.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup), OrderLast = true)]
[UpdateBefore(typeof(CheckDeathSystem))]
[BurstCompile]
public partial struct DeathPuddleSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<DeathPuddle>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var dead = new NativeList<Entity>(Allocator.Temp);
        var puddles = new NativeList<DeathPuddle>(Allocator.Temp);
        var positions = new NativeList<LocalTransform>(Allocator.Temp);
        foreach (var (puddle, health, transform, entity) in SystemAPI.Query<RefRO<DeathPuddle>, RefRO<Health>, RefRO<LocalTransform>>()
                     .WithNone<DeathPuddleLeft>().WithEntityAccess())
        {
            if (health.ValueRO.CurrentValue > 0f) continue;
            dead.Add(entity);
            puddles.Add(puddle.ValueRO);
            positions.Add(LocalTransform.FromPosition(transform.ValueRO.Position));
        }
        if (dead.IsEmpty) return;
        state.EntityManager.AddComponent<DeathPuddleLeft>(dead.AsArray());

        for (int i = 0; i < dead.Length; i++)
        {
            var p = puddles[i];
            var hazard = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(hazard, positions[i]);
            state.EntityManager.AddComponentData(hazard, new Hazard { Type = p.Type, DamagePerSecond = p.DamagePerSecond, Radius = p.Radius, TimeRemaining = p.Lifetime, HitsPlayers = true });
            state.EntityManager.AddComponentData(hazard, new Source { Entity = dead[i] });
        }
    }
}
