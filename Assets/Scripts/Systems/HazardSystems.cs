using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Hazards (see Hazard): every TickInterval, each enemy standing in at least one hazard takes one normal main hit per
/// source and damage type, of DamagePerSecond x TickInterval; it can crit and applies the type's status effect.
/// Overlapping hazards of the same source and type count once (the strongest). Counts hazards down and removes them.
/// </summary>
[UpdateInGroup(typeof(AttackSystemGroup))]
[BurstCompile]
public partial struct HazardSystem : ISystem
{
    public const float TickInterval = 0.5f;

    float _tickTimer;
    Unity.Mathematics.Random _random;

    public void OnCreate(ref SystemState state)
    {
        _random = Unity.Mathematics.Random.CreateFromIndex((uint)Environment.TickCount);
        _tickTimer = TickInterval;
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<Hazard>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        _tickTimer -= deltaTime;
        if (_tickTimer <= 0f)
        {
            _tickTimer = math.max(_tickTimer + TickInterval, 0f);
            Tick(ref state);
        }

        var expired = new NativeList<Entity>(Allocator.Temp);
        foreach (var (hazard, entity) in SystemAPI.Query<RefRW<Hazard>>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            hazard.ValueRW.TimeRemaining -= deltaTime;
            if (hazard.ValueRO.TimeRemaining <= 0f) expired.Add(entity);
        }
        if (!expired.IsEmpty) state.EntityManager.AddComponent<DestroyWithChildren>(expired.AsArray());
    }

    void Tick(ref SystemState state)
    {
        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        var overlaps = new NativeList<DistanceHit>(Allocator.Temp);
        var filter = new CollisionFilter
        {
            BelongsTo = ~0u,
            CollidesWith = (uint)CollisionLayerHelper.AnyEnemy,
            GroupIndex = 0,
        };

        // One hit per enemy, source and type: the strongest hazard it stands in
        var hits = new NativeHashMap<HitKey, HitValue>(16, Allocator.Temp);
        foreach (var (hazard, transform, source) in SystemAPI.Query<RefRO<Hazard>, RefRO<LocalTransform>, RefRO<Source>>().WithNone<DestroyWithChildren>())
        {
            overlaps.Clear();
            physicsWorld.OverlapSphere(transform.ValueRO.Position, hazard.ValueRO.Radius, ref overlaps, filter);
            foreach (var overlap in overlaps)
            {
                var target = overlap.Entity;
                if (!SystemAPI.HasComponent<Enemy>(target) || !SystemAPI.HasComponent<Health>(target) || !SystemAPI.HasBuffer<DamageInstance>(target)) continue;

                var key = new HitKey { Target = target, Source = source.ValueRO.Entity, Type = hazard.ValueRO.Type };
                if (hits.TryGetValue(key, out var existing) && existing.DamagePerSecond >= hazard.ValueRO.DamagePerSecond) continue;
                hits[key] = new HitValue { DamagePerSecond = hazard.ValueRO.DamagePerSecond, Position = transform.ValueRO.Position };
            }
        }

        foreach (var pair in hits)
        {
            var key = pair.Key;
            float damage = pair.Value.DamagePerSecond * TickInterval;
            bool isCrit = false;
            AttackSystem.CalculateCrit(key.Source, key.Target, state.EntityManager, ref _random, ref damage, ref isCrit);
            var targetPosition = SystemAPI.GetComponent<LocalTransform>(key.Target).Position;
            SystemAPI.GetBuffer<DamageInstance>(key.Target).Add(new DamageInstance
            {
                Damage = damage, Type = key.Type, Hierarchy = DamageHierarchy.Main, Source = key.Source, IsCrit = isCrit,
                HitDirection = AttackSystem.HitDirection(pair.Value.Position, targetPosition, ref _random),
            });
        }
    }

    struct HitKey : IEquatable<HitKey>
    {
        public Entity Target;
        public Entity Source;
        public DamageType Type;

        public bool Equals(HitKey other) => Target == other.Target && Source == other.Source && Type == other.Type;
        public override int GetHashCode() => (int)math.hash(new int4(Target.Index, Target.Version, Source.Index, (int)Type));
    }

    struct HitValue
    {
        public float DamagePerSecond;
        public float3 Position;
    }
}

/// <summary>Placeholder art: each hazard as a sphere in its damage type's color.</summary>
[UpdateInGroup(typeof(FeedbackSystemGroup))]
[BurstCompile]
partial struct HazardUISystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (hazard, transform) in SystemAPI.Query<RefRO<Hazard>, RefRO<LocalTransform>>())
            ProgrammerArt.DrawSphere(transform.ValueRO.Position, hazard.ValueRO.Radius, TypeColor(hazard.ValueRO.Type));
    }

    static Color TypeColor(DamageType type) => type switch
    {
        DamageType.Fire => Color.orangeRed,
        DamageType.Cold => Color.cyan,
        DamageType.Lightning => Color.yellow,
        DamageType.Poison => Color.green,
        _ => Color.gray,
    };
}
