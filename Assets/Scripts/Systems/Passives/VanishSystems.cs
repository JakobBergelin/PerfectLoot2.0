using Unity.Burst;
using Unity.Entities;

/// <summary>Vanish: a wearer that used a Mobility skill this frame vanishes for Duration seconds; counts it down. A skill reaction.</summary>
[UpdateInGroup(typeof(SkillReactionSystemGroup))]
[BurstCompile]
public partial struct VanishSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Vanish>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        double now = SystemAPI.Time.ElapsedTime;
        var mobility = SystemAPI.GetComponentLookup<MobilitySkillUsed>(true);

        foreach (var (vanish, entity) in SystemAPI.Query<RefRW<Vanish>>().WithEntityAccess())
        {
            ref var v = ref vanish.ValueRW;
            if (mobility.HasComponent(entity) && mobility.IsComponentEnabled(entity))
            {
                // A new vanish starts over: enemies hit during the last one have to be hit again
                if (v.TimeRemaining <= 0f) v.Since = now;
                v.TimeRemaining = v.Duration;
            }
            else if (v.TimeRemaining > 0f) v.TimeRemaining -= deltaTime;
        }
    }
}

/// <summary>
/// Vanish: enemies after a vanished wearer lose it, unless the wearer hit them since it vanished (their LastHit). After
/// detection has picked targets and before enemies move, so they stand and look around instead of following.
/// </summary>
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateAfter(typeof(EnemyDetectionSystem))]
[UpdateAfter(typeof(HuntPlayerSystem))]
[UpdateBefore(typeof(EnemyMovementSystem))]
[BurstCompile]
public partial struct VanishTargetSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Vanish>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var vanishes = SystemAPI.GetComponentLookup<Vanish>(true);
        var lastHits = SystemAPI.GetComponentLookup<LastHit>(true);

        foreach (var (target, entity) in SystemAPI.Query<RefRW<EnemyTarget>>().WithEntityAccess())
        {
            var unit = target.ValueRO.Unit;
            if (!vanishes.HasComponent(unit) || vanishes[unit].TimeRemaining <= 0f) continue;
            if (lastHits.HasComponent(entity) && lastHits[entity].Source == unit && lastHits[entity].Time >= vanishes[unit].Since) continue;
            target.ValueRW.Unit = Entity.Null;
        }
    }
}
