using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Removes projectiles that got farther than CombatConfig.ProjectileMaxDistance from every player (missed shots would
/// otherwise fly forever). Paused while there is no player; leftovers are cleared once it respawns.
/// </summary>
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateAfter(typeof(ProjectileSystem))]
[BurstCompile]
public partial struct ProjectileCleanupSystem : ISystem
{
    EntityQuery _playerQuery;

    public void OnCreate(ref SystemState state)
    {
        _playerQuery = SystemAPI.QueryBuilder().WithAll<Player, LocalTransform>().Build();
        state.RequireForUpdate<CombatConfig>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (_playerQuery.IsEmpty) return;

        float maxDistance = SystemAPI.GetSingleton<CombatConfig>().ProjectileMaxDistance;
        state.Dependency = new CleanupJob
        {
            Players = _playerQuery.ToComponentDataArray<LocalTransform>(state.WorldUpdateAllocator),
            MaxDistanceSq = maxDistance * maxDistance,
            CommandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter(),
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(Projectile))]
    [WithNone(typeof(DestroyWithChildren))]
    partial struct CleanupJob : IJobEntity
    {
        [ReadOnly] public NativeArray<LocalTransform> Players;
        public float MaxDistanceSq;
        public EntityCommandBuffer.ParallelWriter CommandBuffer;

        void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, in LocalTransform transform)
        {
            for (int i = 0; i < Players.Length; i++)
                if (math.distancesq(transform.Position, Players[i].Position) <= MaxDistanceSq) return;
            CommandBuffer.AddComponent<DestroyWithChildren>(chunkIndex, entity);
        }
    }
}

/// <summary>Adds up how far each ProjectileRange projectile moved this frame and removes it once it has flown its MaxDistance.</summary>
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateAfter(typeof(ProjectileSystem))]
[BurstCompile]
public partial struct ProjectileRangeSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<ProjectileRange>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        foreach (var (range, transform, entity) in SystemAPI.Query<RefRW<ProjectileRange>, RefRO<LocalTransform>>()
                     .WithAll<Projectile>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            var position = transform.ValueRO.Position;
            range.ValueRW.Traveled += math.distance(range.ValueRO.LastPosition, position);
            range.ValueRW.LastPosition = position;
            if (range.ValueRO.Traveled >= range.ValueRO.MaxDistance)
                commandBuffer.AddComponent<DestroyWithChildren>(entity);
        }
    }
}
