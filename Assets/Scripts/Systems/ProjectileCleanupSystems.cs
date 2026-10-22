using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Removes projectiles that got farther than CombatConfig.ProjectileMaxDistance from the player (missed shots would
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
        if (_playerQuery.CalculateEntityCount() != 1) return;

        float maxDistance = SystemAPI.GetSingleton<CombatConfig>().ProjectileMaxDistance;
        state.Dependency = new CleanupJob
        {
            PlayerPosition = SystemAPI.GetComponent<LocalTransform>(_playerQuery.GetSingletonEntity()).Position,
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
        public float3 PlayerPosition;
        public float MaxDistanceSq;
        public EntityCommandBuffer.ParallelWriter CommandBuffer;

        void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, in LocalTransform transform)
        {
            if (math.distancesq(transform.Position, PlayerPosition) > MaxDistanceSq)
                CommandBuffer.AddComponent<DestroyWithChildren>(chunkIndex, entity);
        }
    }
}
