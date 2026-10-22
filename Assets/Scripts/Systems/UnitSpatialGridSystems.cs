using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>One unit in the spatial grid.</summary>
public struct GridUnit
{
    public Entity Entity;
    public float3 Position;
    public float Radius;
}

/// <summary>
/// Singleton: every unit with Steering, bucketed by a horizontal grid cell. Rebuilt each frame by UnitSpatialGridSystem,
/// read by systems that need nearby units (enemy separation). Use Cell / Hash to look up a cell.
/// </summary>
public struct UnitSpatialGrid : IComponentData
{
    public const float CellSize = 2f;
    public NativeParallelMultiHashMap<int, GridUnit> Units;

    public static int2 Cell(float3 position) => (int2)math.floor(position.xz / CellSize);
    public static int Hash(int2 cell) => (int)math.hash(cell);
}

[UpdateInGroup(typeof(MovementSystemGroup), OrderFirst = true)]
[BurstCompile]
public partial struct UnitSpatialGridSystem : ISystem
{
    EntityQuery _unitsQuery;

    public void OnCreate(ref SystemState state)
    {
        _unitsQuery = SystemAPI.QueryBuilder().WithAll<Steering, LocalTransform>().Build();
        state.EntityManager.CreateSingleton(new UnitSpatialGrid
        {
            Units = new NativeParallelMultiHashMap<int, GridUnit>(1024, Allocator.Persistent)
        });
    }

    public void OnDestroy(ref SystemState state)
    {
        if (SystemAPI.TryGetSingletonRW<UnitSpatialGrid>(out var grid) && grid.ValueRO.Units.IsCreated)
        {
            state.CompleteDependency();
            grid.ValueRW.Units.Dispose();
        }
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Readers from last frame must be done before the map is cleared
        var grid = SystemAPI.GetSingletonRW<UnitSpatialGrid>();
        state.CompleteDependency();

        var units = grid.ValueRW.Units;
        units.Clear();
        int count = _unitsQuery.CalculateEntityCount();
        if (units.Capacity < count) units.Capacity = count * 2;

        state.Dependency = new FillGridJob { Units = units.AsParallelWriter() }.ScheduleParallel(_unitsQuery, state.Dependency);
    }

    [BurstCompile]
    partial struct FillGridJob : IJobEntity
    {
        public NativeParallelMultiHashMap<int, GridUnit>.ParallelWriter Units;

        void Execute(Entity entity, in LocalTransform transform, in Steering steering)
        {
            Units.Add(UnitSpatialGrid.Hash(UnitSpatialGrid.Cell(transform.Position)),
                new GridUnit { Entity = entity, Position = transform.Position, Radius = steering.Radius });
        }
    }
}
