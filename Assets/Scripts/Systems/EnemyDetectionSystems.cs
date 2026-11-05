using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>A living unit enemies can go after this frame.</summary>
public struct EnemyTargetUnit
{
    public Entity Entity;
    public float3 Position;
    public int FieldSlot;
    public float Threat;
}

/// <summary>Singleton: every living EnemyTargetable unit, rebuilt each frame by EnemyTargetsSystem.</summary>
public struct EnemyTargets : IComponentData
{
    public NativeList<EnemyTargetUnit> Units;

    /// <summary>The index of entity in units, -1 when it isn't a living target.</summary>
    public static int Find(in NativeArray<EnemyTargetUnit> units, Entity entity)
    {
        for (int i = 0; i < units.Length; i++)
            if (units[i].Entity == entity) return i;
        return -1;
    }

    /// <summary>The index of the unit nearest to position (horizontally, counting its Threat) within maxDistance, -1 when there are none.</summary>
    public static int Nearest(in NativeArray<EnemyTargetUnit> units, float3 position, float maxDistance = float.MaxValue)
    {
        int nearest = -1;
        float nearestScore = float.MaxValue;
        for (int i = 0; i < units.Length; i++)
        {
            float distance = math.distance(units[i].Position.xz, position.xz);
            float score = distance - units[i].Threat;
            if (distance > maxDistance || score >= nearestScore) continue;
            nearestScore = score;
            nearest = i;
        }
        return nearest;
    }
}

/// <summary>Lists the units enemies can go after. A dead (or dying) unit is no target.</summary>
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateBefore(typeof(FlowFieldSystem))]
[BurstCompile]
public partial struct EnemyTargetsSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.EntityManager.CreateSingleton(new EnemyTargets { Units = new NativeList<EnemyTargetUnit>(8, Allocator.Persistent) });
    }

    public void OnDestroy(ref SystemState state)
    {
        if (SystemAPI.TryGetSingletonRW<EnemyTargets>(out var targets))
        {
            state.CompleteDependency();
            if (targets.ValueRO.Units.IsCreated) targets.ValueRW.Units.Dispose();
        }
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Last frame's readers must be done before it's rebuilt
        var targets = SystemAPI.GetSingletonRW<EnemyTargets>();
        state.CompleteDependency();
        var units = targets.ValueRW.Units;
        units.Clear();

        foreach (var (targetable, transform, health, entity) in SystemAPI.Query<RefRO<EnemyTargetable>, RefRO<LocalTransform>, RefRO<Health>>().WithNone<RigidDeath>().WithEntityAccess())
        {
            if (health.ValueRO.CurrentValue <= 0f) continue;
            units.Add(new EnemyTargetUnit { Entity = entity, Position = transform.ValueRO.Position, FieldSlot = targetable.ValueRO.FieldSlot, Threat = targetable.ValueRO.Threat });
        }
    }
}

/// <summary>Something that makes enemies near it notice a unit: its attack (near miss) or an enemy that found it.</summary>
public struct EnemyAlert
{
    public float3 Position;
    public float Radius;
    public Entity Unit;
    public bool NearMiss;   // An attack: each enemy adds its NearMissMargin to Radius
}

/// <summary>
/// Singleton: alerts posted this frame (Pending: attacks, enemies that found a target) and the ones enemies react
/// to (Current: last frame's). Swapped by EnemyDetectionSystem.
/// </summary>
public struct EnemyAlerts : IComponentData
{
    public NativeQueue<EnemyAlert> Pending;
    public NativeList<EnemyAlert> Current;

    /// <summary>The first living target an alert about it reaches an enemy at position with (horizontally).</summary>
    public static bool TryReach(in NativeArray<EnemyAlert> alerts, in NativeArray<EnemyTargetUnit> targets, float3 position, float nearMissMargin, out Entity unit)
    {
        for (int i = 0; i < alerts.Length; i++)
        {
            var alert = alerts[i];
            float radius = alert.Radius + (alert.NearMiss ? nearMissMargin : 0f);
            if (math.distancesq(alert.Position.xz, position.xz) > radius * radius || EnemyTargets.Find(targets, alert.Unit) < 0) continue;
            unit = alert.Unit;
            return true;
        }
        unit = Entity.Null;
        return false;
    }
}

/// <summary>
/// Sets each enemy's EnemyTarget to the nearest target it senses, counting Threat. Physics enemies (EnemyDetection): see it within their
/// sight radius (shrinking toward ProximityRadius for a target behind them) with line of sight on the FlowField, or notice
/// it within ProximityRadius through walls; finding one themselves alerts enemies within AlertRadius (next frame). Swarms
/// (SwarmDetection): a target within Radius, keeping it unless one with more Threat comes that close. Both react to alerts and drop a dead or gone target; Physics enemies also
/// forget it after ForgetTime without sensing it. Being hit is EnemyHitDetectionSystem, near misses EnemyNearMissSystem.
/// </summary>
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateAfter(typeof(FlowFieldSystem))]
[UpdateBefore(typeof(EnemyMovementSystem))]
[BurstCompile]
public partial struct EnemyDetectionSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.EntityManager.CreateSingleton(new EnemyAlerts
        {
            Pending = new NativeQueue<EnemyAlert>(Allocator.Persistent),
            Current = new NativeList<EnemyAlert>(64, Allocator.Persistent),
        });
        state.RequireForUpdate<FlowFields>();
        state.RequireForUpdate<EnemyTargets>();
    }

    public void OnDestroy(ref SystemState state)
    {
        if (SystemAPI.TryGetSingletonRW<EnemyAlerts>(out var alerts))
        {
            state.CompleteDependency();
            if (alerts.ValueRO.Pending.IsCreated) alerts.ValueRW.Pending.Dispose();
            if (alerts.ValueRO.Current.IsCreated) alerts.ValueRW.Current.Dispose();
        }
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Last frame's alert writers (hits, near misses, this job) must be done before they're swapped in
        var alertsRW = SystemAPI.GetSingletonRW<EnemyAlerts>();
        state.CompleteDependency();
        ref var alerts = ref alertsRW.ValueRW;
        alerts.Current.Clear();
        while (alerts.Pending.TryDequeue(out var alert)) alerts.Current.Add(alert);

        var targets = SystemAPI.GetSingleton<EnemyTargets>().Units.AsArray();
        var current = alerts.Current.AsArray();
        state.Dependency = new DetectionJob
        {
            Targets = targets,
            DeltaTime = SystemAPI.Time.DeltaTime,
            Fields = SystemAPI.GetSingleton<FlowFields>(),
            Alerts = current,
            NewAlerts = alerts.Pending.AsParallelWriter(),
        }.ScheduleParallel(state.Dependency);

        state.Dependency = new SwarmDetectionJob
        {
            Targets = targets,
            Alerts = current,
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    partial struct DetectionJob : IJobEntity
    {
        [ReadOnly] public NativeArray<EnemyTargetUnit> Targets;
        public float DeltaTime;
        [ReadOnly] public FlowFields Fields;
        [ReadOnly] public NativeArray<EnemyAlert> Alerts;
        public NativeQueue<EnemyAlert>.ParallelWriter NewAlerts;

        void Execute(ref EnemyTarget target, in EnemyDetection detection, in LocalTransform transform)
        {
            if (EnemyTargets.Find(Targets, target.Unit) < 0) target.Unit = Entity.Null;
            if (Targets.Length == 0) return;
            target.SinceSensed += DeltaTime;

            var found = NearestSensed(detection, transform);
            if (found != Entity.Null)
            {
                // Found it itself (not alerted): the enemies around it join in
                if (target.Unit == Entity.Null && detection.AlertRadius > 0f)
                    NewAlerts.Enqueue(new EnemyAlert { Position = transform.Position, Radius = detection.AlertRadius, Unit = found });
                target.Unit = found;
                target.SinceSensed = 0f;
                return;
            }

            if (EnemyAlerts.TryReach(Alerts, Targets, transform.Position, detection.NearMissMargin, out var alerted))
            {
                target.Unit = alerted;
                target.SinceSensed = 0f;
                return;
            }

            if (target.Unit != Entity.Null && target.SinceSensed > detection.ForgetTime)
                target.Unit = Entity.Null;
        }

        // Close enough to notice through walls; otherwise seen, the sight radius shrinking toward the proximity radius as
        // the target gets behind it (from its side to straight behind)
        Entity NearestSensed(in EnemyDetection detection, in LocalTransform transform)
        {
            var position = transform.Position;
            var forward = math.mul(transform.Rotation, new float3(0f, 0f, 1f));
            forward = math.normalizesafe(new float3(forward.x, 0f, forward.z));

            var nearest = Entity.Null;
            float nearestScore = float.MaxValue;
            for (int i = 0; i < Targets.Length; i++)
            {
                var unit = Targets[i];
                var toUnit = unit.Position - position;
                toUnit.y = 0f;
                float distance = math.length(toUnit);
                float score = distance - unit.Threat;
                if (score >= nearestScore) continue;

                bool found = distance <= detection.ProximityRadius;
                if (!found)
                {
                    float behind = math.saturate(-math.dot(forward, toUnit / math.max(distance, 1e-4f)));
                    float sight = math.lerp(detection.SightRadius, detection.ProximityRadius, behind);
                    found = distance <= sight && Fields[unit.FieldSlot].CanSee(position, unit.Position);
                }
                if (!found) continue;
                nearest = unit.Entity;
                nearestScore = score;
            }
            return nearest;
        }
    }

    [BurstCompile]
    partial struct SwarmDetectionJob : IJobEntity
    {
        [ReadOnly] public NativeArray<EnemyTargetUnit> Targets;
        [ReadOnly] public NativeArray<EnemyAlert> Alerts;

        void Execute(ref EnemyTarget target, in SwarmDetection detection, in LocalTransform transform)
        {
            int current = EnemyTargets.Find(Targets, target.Unit);
            if (current < 0) target.Unit = Entity.Null;
            if (Targets.Length == 0) return;

            var position = transform.Position;
            int nearest = EnemyTargets.Nearest(Targets, position, detection.Radius);
            if (nearest >= 0 && (current < 0 || Targets[nearest].Threat > Targets[current].Threat))
                target.Unit = Targets[nearest].Entity;
            else if (current < 0 && EnemyAlerts.TryReach(Alerts, Targets, position, detection.NearMissMargin, out var alerted))
                target.Unit = alerted;
        }
    }
}

/// <summary>
/// Enemies hit by a target (damage this frame whose Source is EnemyTargetable) go after it. A Physics enemy that wasn't after
/// anything yet alerts the enemies within its AlertRadius (next frame). Before CalculateDamageSystem clears the damage.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateBefore(typeof(CalculateDamageSystem))]
[BurstCompile]
public partial struct EnemyHitDetectionSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EnemyAlerts>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new HitJob
        {
            Targetables = SystemAPI.GetComponentLookup<EnemyTargetable>(true),
            Detections = SystemAPI.GetComponentLookup<EnemyDetection>(true),
            NewAlerts = SystemAPI.GetSingletonRW<EnemyAlerts>().ValueRW.Pending.AsParallelWriter(),
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    partial struct HitJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<EnemyTargetable> Targetables;
        [ReadOnly] public ComponentLookup<EnemyDetection> Detections;
        public NativeQueue<EnemyAlert>.ParallelWriter NewAlerts;

        void Execute(Entity entity, ref EnemyTarget target, in LocalTransform transform, in DynamicBuffer<DamageInstance> damage)
        {
            for (int i = 0; i < damage.Length; i++)
            {
                var source = damage[i].Source;
                if (!Targetables.HasComponent(source)) continue;

                if (target.Unit == Entity.Null && Detections.HasComponent(entity) && Detections[entity].AlertRadius > 0f)
                    NewAlerts.Enqueue(new EnemyAlert { Position = transform.Position, Radius = Detections[entity].AlertRadius, Unit = source });
                target.Unit = source;
                target.SinceSensed = 0f;
                return;
            }
        }
    }
}

/// <summary>
/// Near misses: every attack (and damage zone) by a target alerts the enemies within its radius plus their NearMissMargin
/// (next frame, EnemyDetectionSystem).
/// </summary>
[UpdateInGroup(typeof(AttackSystemGroup))]
[UpdateAfter(typeof(AttackSystem))]
[BurstCompile]
public partial struct EnemyNearMissSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EnemyAlerts>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var alertsRW = SystemAPI.GetSingletonRW<EnemyAlerts>();
        state.CompleteDependency();
        var pending = alertsRW.ValueRW.Pending;
        var targetables = SystemAPI.GetComponentLookup<EnemyTargetable>(true);

        foreach (var (attack, transform, source, entity) in SystemAPI.Query<RefRO<Attack>, RefRO<LocalTransform>, RefRO<Source>>().WithEntityAccess())
        {
            if (!targetables.HasComponent(source.ValueRO.Entity)) continue;
            if (SystemAPI.HasComponent<AttackTarget>(entity) && SystemAPI.GetComponent<AttackTarget>(entity).TargetType == TargetType.Player) continue;
            pending.Enqueue(new EnemyAlert { Position = transform.ValueRO.Position, Radius = attack.ValueRO.Radius, Unit = source.ValueRO.Entity, NearMiss = true });
        }
        foreach (var (attack, transform, source) in SystemAPI.Query<RefRO<PeriodicAttack>, RefRO<LocalTransform>, RefRO<Source>>())
        {
            if (!targetables.HasComponent(source.ValueRO.Entity)) continue;
            pending.Enqueue(new EnemyAlert { Position = transform.ValueRO.Position, Radius = attack.ValueRO.Radius, Unit = source.ValueRO.Entity, NearMiss = true });
        }
    }
}

/// <summary>
/// Enemies with HuntsPlayer target the nearest living target wherever it is, after the detection systems (which would drop
/// one they can't sense) and before EnemyMovementSystem reads the target.
/// </summary>
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateAfter(typeof(EnemyDetectionSystem))]
[UpdateBefore(typeof(EnemyMovementSystem))]
[BurstCompile]
public partial struct HuntPlayerSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<HuntsPlayer>();
        state.RequireForUpdate<EnemyTargets>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new HuntJob { Targets = SystemAPI.GetSingleton<EnemyTargets>().Units.AsArray() }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(HuntsPlayer))]
    partial struct HuntJob : IJobEntity
    {
        [ReadOnly] public NativeArray<EnemyTargetUnit> Targets;

        void Execute(ref EnemyTarget target, in LocalTransform transform)
        {
            int nearest = EnemyTargets.Nearest(Targets, transform.Position);
            target.Unit = nearest >= 0 ? Targets[nearest].Entity : Entity.Null;
            target.SinceSensed = 0f;
        }
    }
}
