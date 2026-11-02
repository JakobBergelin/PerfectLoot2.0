using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

/// <summary>Static Field legendary power, added by StaticFieldPower while the item is equipped.</summary>
public struct StaticField : IComponentData
{
    public float Interval;              // Seconds between zaps
    public float Range;                 // Meters from the wearer
    public float Damage;                // Lightning, before the wearer's damage %
    public float ResistancePerStack;    // Lightning resistance each Conductive stack takes away
    public int MaxStacks;
    public float StackDuration;         // Seconds Conductive lasts after the last zap

    public float Elapsed;               // Up to Interval; waits there until an enemy is in range
}

/// <summary>
/// Static Field: every Interval, zaps the enemy nearest the wearer within Range with a normal main hit (damage %,
/// conversion and crits apply; lightning's Charged too, as its only target) and adds a Conductive stack to it, whatever
/// the zap's element. Waits with the zap ready while no enemy is in range.
/// </summary>
[UpdateInGroup(typeof(AttackSystemGroup))]
[BurstCompile]
public partial struct StaticFieldSystem : ISystem
{
    Unity.Mathematics.Random _random;

    public void OnCreate(ref SystemState state)
    {
        _random = Unity.Mathematics.Random.CreateFromIndex((uint)Environment.TickCount);
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<StaticField>();
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
        var conductive = SystemAPI.GetComponentLookup<Conductive>();

        foreach (var (staticField, stats, transform, entity) in SystemAPI.Query<RefRW<StaticField>, RefRO<CalculatedStats>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            ref var s = ref staticField.ValueRW;
            s.Elapsed = math.min(s.Elapsed + deltaTime, s.Interval);
            if (s.Elapsed < s.Interval) continue;

            var position = transform.ValueRO.Position;
            hits.Clear();
            physicsWorld.OverlapSphere(position, s.Range, ref hits, filter);
            var target = Entity.Null;
            float nearest = float.MaxValue;
            float3 targetPosition = default;
            foreach (var hit in hits)
            {
                if (!SystemAPI.HasComponent<Enemy>(hit.Entity) || !SystemAPI.HasComponent<Health>(hit.Entity) || !SystemAPI.HasBuffer<DamageInstance>(hit.Entity)) continue;
                var hitPosition = SystemAPI.GetComponent<LocalTransform>(hit.Entity).Position;
                float distanceSq = math.distancesq(position, hitPosition);
                if (distanceSq >= nearest) continue;
                nearest = distanceSq;
                target = hit.Entity;
                targetPosition = hitPosition;
            }
            if (target == Entity.Null) continue;
            s.Elapsed = 0f;

            var type = stats.ValueRO.Value.ConvertedType(DamageType.Lightning);
            float damage = s.Damage * stats.ValueRO.Value.DamageMultiplier(type);
            bool isCrit = false;
            AttackSystem.CalculateCrit(entity, target, state.EntityManager, ref _random, ref damage, ref isCrit);
            SystemAPI.GetBuffer<DamageInstance>(target).Add(new DamageInstance
            {
                Damage = damage, Type = type, Hierarchy = DamageHierarchy.Main, Source = entity, IsCrit = isCrit, IsCenterTarget = true,
                HitDirection = AttackSystem.HitDirection(position, targetPosition, ref _random),
            });

            if (conductive.HasComponent(target))
            {
                var debuff = EffectApply.Refresh(conductive, target, s.StackDuration);
                debuff.ValueRW.Stacks = math.min(debuff.ValueRO.Stacks + 1, math.max(1, s.MaxStacks));
                debuff.ValueRW.ResistancePerStack = s.ResistancePerStack;
            }

            // Placeholder art: the zap as a short-lived line
            Debug.DrawLine(position + new float3(0f, 1f, 0f), targetPosition + new float3(0f, 1f, 0f), Color.yellow, 0.15f);
        }
    }
}

/// <summary>Takes ResistancePerStack lightning resistance per Conductive stack away from the unit.</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct ConductiveStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (debuff, stats) in SystemAPI.Query<RefRO<Conductive>, RefRW<CalculatedStats>>())
            stats.ValueRW.Value.LightningResistance -= debuff.ValueRO.Stacks * debuff.ValueRO.ResistancePerStack;
    }
}
