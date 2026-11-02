using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>Counter Strike legendary power, added by CounterStrikePower while the item is equipped.</summary>
public struct CounterStrike : IComponentData
{
    public int HitsNeeded;          // Hits taken that set off the strike
    public float Damage;            // Physical, before the wearer's damage %
    public float Radius;            // Scaled by the wearer's area of effect
    public float SpinDuration;      // Seconds for the wearer's full turn

    public int Hits;                // Taken since the last strike
    public float SpinElapsed;       // Up to SpinDuration while spinning
    public bool Spinning;
    public quaternion SpinStart;    // The wearer's facing when the spin started
}

/// <summary>
/// Counter Strike: every main hit the wearer takes that still deals damage (after armor, block, Refraction and
/// Invulnerable) counts; at HitsNeeded the wearer spins and enemies within Radius take Damage as a normal main physical
/// hit by the wearer: damage %, element conversion, crits and bleeding apply. Several hits in one frame set it off once
/// and the rest carry over. The count is shown on the HUD (CounterStrikeCharge). Between working out the damage and
/// dealing it, so blocked and absorbed hits are already 0; its own hits are dealt next frame.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateBefore(typeof(DamagingSystem))]
[BurstCompile]
public partial struct CounterStrikeSystem : ISystem
{
    Random _random;

    public void OnCreate(ref SystemState state)
    {
        _random = Random.CreateFromIndex((uint)System.Environment.TickCount);
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<CounterStrike>();
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
        var chargeLookup = SystemAPI.GetComponentLookup<CounterStrikeCharge>();

        foreach (var (counterStrike, taken, stats, transform, entity) in
                 SystemAPI.Query<RefRW<CounterStrike>, DynamicBuffer<CalculatedDamageInstance>, RefRO<CalculatedStats>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            ref var c = ref counterStrike.ValueRW;
            int hitsNeeded = math.max(1, c.HitsNeeded);
            foreach (var instance in taken)
                if (instance.Hierarchy == DamageHierarchy.Main && instance.Damage > 0f) c.Hits++;

            if (c.Hits >= hitsNeeded)
            {
                c.Hits -= hitsNeeded;
                c.Hits = math.min(c.Hits, hitsNeeded - 1);
                c.Spinning = true;
                c.SpinElapsed = 0f;
                c.SpinStart = transform.ValueRO.Rotation;
                Strike(ref state, physicsWorld, hits, filter, entity, c, stats.ValueRO.Value, transform.ValueRO.Position);
            }

            if (chargeLookup.HasComponent(entity))
            {
                chargeLookup[entity] = new CounterStrikeCharge { Stacks = c.Hits };
                chargeLookup.SetComponentEnabled(entity, c.Hits > 0);
            }
        }
    }

    void Strike(ref SystemState state, in PhysicsWorldSingleton physicsWorld, NativeList<DistanceHit> hits, CollisionFilter filter, Entity entity,
                in CounterStrike counterStrike, in StatBlock stats, float3 position)
    {
        var type = stats.ConvertedType(DamageType.Physical);
        float baseDamage = counterStrike.Damage * stats.DamageMultiplier(type);

        hits.Clear();
        physicsWorld.OverlapSphere(position, counterStrike.Radius * stats.AreaMultiplier, ref hits, filter);
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

/// <summary>
/// Turns a Counter Strike wearer one full turn over SpinDuration from where it faced when the strike went off. Last,
/// after movement and skills have set its facing, so the spin shows; it ends facing the same way it started.
/// </summary>
[UpdateInGroup(typeof(FeedbackSystemGroup))]
[BurstCompile]
public partial struct CounterStrikeSpinSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<CounterStrike>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        foreach (var (counterStrike, transform) in SystemAPI.Query<RefRW<CounterStrike>, RefRW<LocalTransform>>())
        {
            ref var c = ref counterStrike.ValueRW;
            if (!c.Spinning) continue;

            c.SpinElapsed += deltaTime;
            float t = c.SpinDuration > 0f ? math.saturate(c.SpinElapsed / c.SpinDuration) : 1f;
            transform.ValueRW.Rotation = math.mul(c.SpinStart, quaternion.RotateY(t * 2f * math.PI));
            if (t >= 1f) c.Spinning = false;
        }
    }
}
