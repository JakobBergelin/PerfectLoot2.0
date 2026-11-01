using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>Something that makes enemies near it notice the player: a player attack (near miss) or an enemy that found the player.</summary>
public struct EnemyAlert
{
    public float3 Position;
    public float Radius;
    public Entity Player;
    public bool NearMiss;   // A player attack: each enemy adds its NearMissMargin to Radius
}

/// <summary>
/// Singleton: alerts posted this frame (Pending: player attacks, enemies that found the player) and the ones enemies react
/// to (Current: last frame's). Swapped by EnemyDetectionSystem.
/// </summary>
public struct EnemyAlerts : IComponentData
{
    public NativeQueue<EnemyAlert> Pending;
    public NativeList<EnemyAlert> Current;

    /// <summary>Whether any alert about player reaches an enemy at position (horizontally).</summary>
    public static bool Reaches(in NativeArray<EnemyAlert> alerts, float3 position, float nearMissMargin, Entity player)
    {
        for (int i = 0; i < alerts.Length; i++)
        {
            var alert = alerts[i];
            if (alert.Player != player) continue;
            float radius = alert.Radius + (alert.NearMiss ? nearMissMargin : 0f);
            if (math.distancesq(alert.Position.xz, position.xz) <= radius * radius) return true;
        }
        return false;
    }
}

/// <summary>
/// Sets each enemy's EnemyTarget. Physics enemies (EnemyDetection): see the player within their sight radius (shrinking
/// toward ProximityRadius for a player behind them) with line of sight on the FlowField, or notice it within
/// ProximityRadius through walls; finding it themselves alerts enemies within AlertRadius (next frame). Swarms
/// (SwarmDetection): the player within Radius. Both react to alerts and drop a dead or gone player; Physics enemies also
/// forget it after ForgetTime without sensing it. Being hit is EnemyHitDetectionSystem, near misses EnemyNearMissSystem.
/// </summary>
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateAfter(typeof(FlowFieldSystem))]
[UpdateBefore(typeof(EnemyMovementSystem))]
[BurstCompile]
public partial struct EnemyDetectionSystem : ISystem
{
    EntityQuery _playerQuery;

    public void OnCreate(ref SystemState state)
    {
        _playerQuery = SystemAPI.QueryBuilder().WithAll<Player, LocalTransform>().Build();
        state.EntityManager.CreateSingleton(new EnemyAlerts
        {
            Pending = new NativeQueue<EnemyAlert>(Allocator.Persistent),
            Current = new NativeList<EnemyAlert>(64, Allocator.Persistent),
        });
        state.RequireForUpdate<FlowField>();
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

        // A dead (or dying) player is no target
        var player = Entity.Null;
        var playerPosition = float3.zero;
        if (_playerQuery.CalculateEntityCount() == 1)
        {
            var entity = _playerQuery.GetSingletonEntity();
            bool dead = (SystemAPI.HasComponent<Health>(entity) && SystemAPI.GetComponent<Health>(entity).CurrentValue <= 0f)
                     || SystemAPI.HasComponent<RigidDeath>(entity);
            if (!dead)
            {
                player = entity;
                playerPosition = SystemAPI.GetComponent<LocalTransform>(entity).Position;
            }
        }

        var current = alerts.Current.AsArray();
        state.Dependency = new DetectionJob
        {
            Player = player,
            PlayerPosition = playerPosition,
            DeltaTime = SystemAPI.Time.DeltaTime,
            Field = SystemAPI.GetSingleton<FlowField>(),
            Alerts = current,
            NewAlerts = alerts.Pending.AsParallelWriter(),
        }.ScheduleParallel(state.Dependency);

        state.Dependency = new SwarmDetectionJob
        {
            Player = player,
            PlayerPosition = playerPosition,
            Alerts = current,
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    partial struct DetectionJob : IJobEntity
    {
        public Entity Player;
        public float3 PlayerPosition;
        public float DeltaTime;
        [ReadOnly] public FlowField Field;
        [ReadOnly] public NativeArray<EnemyAlert> Alerts;
        public NativeQueue<EnemyAlert>.ParallelWriter NewAlerts;

        void Execute(ref EnemyTarget target, in EnemyDetection detection, in LocalTransform transform)
        {
            if (target.Player != Player) target.Player = Entity.Null;
            if (Player == Entity.Null) return;
            target.SinceSensed += DeltaTime;

            var position = transform.Position;
            var toPlayer = PlayerPosition - position;
            toPlayer.y = 0f;
            float distance = math.length(toPlayer);

            // Close enough to notice through walls; otherwise seen, the sight radius shrinking toward the proximity
            // radius as the player gets behind it (from its side to straight behind)
            bool found = distance <= detection.ProximityRadius;
            if (!found)
            {
                var forward = math.mul(transform.Rotation, new float3(0f, 0f, 1f));
                forward = math.normalizesafe(new float3(forward.x, 0f, forward.z));
                float behind = math.saturate(-math.dot(forward, toPlayer / math.max(distance, 1e-4f)));
                float sight = math.lerp(detection.SightRadius, detection.ProximityRadius, behind);
                found = distance <= sight && Field.CanSee(position, PlayerPosition);
            }
            if (found)
            {
                // Found it itself (not alerted): the enemies around it join in
                if (target.Player == Entity.Null && detection.AlertRadius > 0f)
                    NewAlerts.Enqueue(new EnemyAlert { Position = position, Radius = detection.AlertRadius, Player = Player });
                target.Player = Player;
                target.SinceSensed = 0f;
                return;
            }

            if (EnemyAlerts.Reaches(Alerts, position, detection.NearMissMargin, Player))
            {
                target.Player = Player;
                target.SinceSensed = 0f;
                return;
            }

            if (target.Player != Entity.Null && target.SinceSensed > detection.ForgetTime)
                target.Player = Entity.Null;
        }
    }

    [BurstCompile]
    partial struct SwarmDetectionJob : IJobEntity
    {
        public Entity Player;
        public float3 PlayerPosition;
        [ReadOnly] public NativeArray<EnemyAlert> Alerts;

        void Execute(ref EnemyTarget target, in SwarmDetection detection, in LocalTransform transform)
        {
            if (target.Player != Player) target.Player = Entity.Null;
            if (Player == Entity.Null || target.Player != Entity.Null) return;

            var position = transform.Position;
            if (math.distancesq(position.xz, PlayerPosition.xz) <= detection.Radius * detection.Radius
                || EnemyAlerts.Reaches(Alerts, position, detection.NearMissMargin, Player))
                target.Player = Player;
        }
    }
}

/// <summary>
/// Enemies hit by the player (damage this frame whose Source is a player) target it. A Physics enemy that wasn't after the
/// player yet alerts the enemies within its AlertRadius (next frame). Before CalculateDamageSystem clears the damage.
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
            Players = SystemAPI.GetComponentLookup<Player>(true),
            Detections = SystemAPI.GetComponentLookup<EnemyDetection>(true),
            NewAlerts = SystemAPI.GetSingletonRW<EnemyAlerts>().ValueRW.Pending.AsParallelWriter(),
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    partial struct HitJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<Player> Players;
        [ReadOnly] public ComponentLookup<EnemyDetection> Detections;
        public NativeQueue<EnemyAlert>.ParallelWriter NewAlerts;

        void Execute(Entity entity, ref EnemyTarget target, in LocalTransform transform, in DynamicBuffer<DamageInstance> damage)
        {
            for (int i = 0; i < damage.Length; i++)
            {
                var source = damage[i].Source;
                if (!Players.HasComponent(source)) continue;

                if (target.Player == Entity.Null && Detections.HasComponent(entity) && Detections[entity].AlertRadius > 0f)
                    NewAlerts.Enqueue(new EnemyAlert { Position = transform.Position, Radius = Detections[entity].AlertRadius, Player = source });
                target.Player = source;
                target.SinceSensed = 0f;
                return;
            }
        }
    }
}

/// <summary>
/// Near misses: every player attack (and damage zone) alerts the enemies within its radius plus their NearMissMargin
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
        var players = SystemAPI.GetComponentLookup<Player>(true);

        foreach (var (attack, transform, source, entity) in SystemAPI.Query<RefRO<Attack>, RefRO<LocalTransform>, RefRO<Source>>().WithEntityAccess())
        {
            if (!players.HasComponent(source.ValueRO.Entity)) continue;
            if (SystemAPI.HasComponent<AttackTarget>(entity) && SystemAPI.GetComponent<AttackTarget>(entity).TargetType == TargetType.Player) continue;
            pending.Enqueue(new EnemyAlert { Position = transform.ValueRO.Position, Radius = attack.ValueRO.Radius, Player = source.ValueRO.Entity, NearMiss = true });
        }
        foreach (var (attack, transform, source) in SystemAPI.Query<RefRO<PeriodicAttack>, RefRO<LocalTransform>, RefRO<Source>>())
        {
            if (!players.HasComponent(source.ValueRO.Entity)) continue;
            pending.Enqueue(new EnemyAlert { Position = transform.ValueRO.Position, Radius = attack.ValueRO.Radius, Player = source.ValueRO.Entity, NearMiss = true });
        }
    }
}

/// <summary>
/// Enemies with HuntsPlayer target the living player wherever it is, after the detection systems (which would drop a
/// player they can't sense) and before EnemyMovementSystem reads the target.
/// </summary>
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateAfter(typeof(EnemyDetectionSystem))]
[UpdateBefore(typeof(EnemyMovementSystem))]
[BurstCompile]
public partial struct HuntPlayerSystem : ISystem
{
    EntityQuery _playerQuery;

    public void OnCreate(ref SystemState state)
    {
        _playerQuery = SystemAPI.QueryBuilder().WithAll<Player>().Build();
        state.RequireForUpdate<HuntsPlayer>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // A dead (or dying) player is no target, like in EnemyDetectionSystem
        var player = Entity.Null;
        if (_playerQuery.CalculateEntityCount() == 1)
        {
            var entity = _playerQuery.GetSingletonEntity();
            bool dead = (SystemAPI.HasComponent<Health>(entity) && SystemAPI.GetComponent<Health>(entity).CurrentValue <= 0f)
                     || SystemAPI.HasComponent<RigidDeath>(entity);
            if (!dead) player = entity;
        }
        state.Dependency = new HuntJob { Player = player }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(HuntsPlayer))]
    partial struct HuntJob : IJobEntity
    {
        public Entity Player;

        void Execute(ref EnemyTarget target)
        {
            target.Player = Player;
            target.SinceSensed = 0f;
        }
    }
}
