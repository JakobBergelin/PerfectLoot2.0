using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

/// <summary>Stormcall legendary power, added by StormcallPower while the item is equipped.</summary>
public struct Stormcall : IComponentData
{
    public float Interval;  // Seconds of channeling between strikes
    public float Range;     // Meters from the wearer
    public float Damage;    // Lightning, before the wearer's damage %

    public float Elapsed;   // Up to Interval; waits there until an enemy is in range
}

/// <summary>
/// Stormcall: while the wearer is Channeling (Whirlwind, Flamethrower), every Interval lightning strikes a random enemy
/// within Range with a normal main hit (damage %, conversion and crits apply; lightning's Charged too, as its only
/// target). The time only runs while channeling; a strike that is ready waits for an enemy in range.
/// </summary>
[UpdateInGroup(typeof(AttackSystemGroup))]
[BurstCompile]
public partial struct StormcallSystem : ISystem
{
    Unity.Mathematics.Random _random;

    public void OnCreate(ref SystemState state)
    {
        _random = Unity.Mathematics.Random.CreateFromIndex((uint)Environment.TickCount);
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<Stormcall>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        var hits = new NativeList<DistanceHit>(Allocator.Temp);
        var filter = new CollisionFilter
        {
            BelongsTo = ~0u,
            CollidesWith = (uint)CollisionLayerHelper.AnyEnemy,
            GroupIndex = 0,
        };

        foreach (var (stormcall, stats, transform, entity) in
                 SystemAPI.Query<RefRW<Stormcall>, RefRO<CalculatedStats>, RefRO<LocalTransform>>().WithAll<Channeling>().WithEntityAccess())
        {
            ref var s = ref stormcall.ValueRW;
            s.Elapsed = math.min(s.Elapsed + deltaTime, s.Interval);
            if (s.Elapsed < s.Interval) continue;

            var position = transform.ValueRO.Position;
            hits.Clear();
            physicsWorld.OverlapSphere(position, s.Range, ref hits, filter);

            // Picks evenly among the enemies in range
            var target = Entity.Null;
            int candidates = 0;
            foreach (var hit in hits)
            {
                if (!SystemAPI.HasComponent<Enemy>(hit.Entity) || !SystemAPI.HasComponent<Health>(hit.Entity) || !SystemAPI.HasBuffer<DamageInstance>(hit.Entity)) continue;
                candidates++;
                if (_random.NextInt(candidates) == 0) target = hit.Entity;
            }
            if (target == Entity.Null) continue;
            s.Elapsed = 0f;

            var targetPosition = SystemAPI.GetComponent<LocalTransform>(target).Position;
            var type = stats.ValueRO.Value.ConvertedType(DamageType.Lightning);
            float damage = s.Damage * stats.ValueRO.Value.DamageMultiplier(type);
            bool isCrit = false;
            AttackSystem.CalculateCrit(entity, target, state.EntityManager, ref _random, ref damage, ref isCrit);
            SystemAPI.GetBuffer<DamageInstance>(target).Add(new DamageInstance
            {
                Damage = damage, Type = type, Hierarchy = DamageHierarchy.Main, Origin = DamageLabel.Stormcall, Source = entity, IsCrit = isCrit, IsCenterTarget = true,
                HitDirection = AttackSystem.HitDirection(position, targetPosition, ref _random),
            });

            // Placeholder art: the strike as a short-lived line from above
            Debug.DrawLine(targetPosition + new float3(0f, 8f, 0f), targetPosition, Color.yellow, 0.15f);
        }
    }
}
