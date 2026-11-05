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
/// read by systems that need nearby units (Separation). Use Cell / Hash to look up a cell.
/// </summary>
public struct UnitSpatialGrid : IComponentData
{
    public const float CellSize = 2f;
    public NativeParallelMultiHashMap<int, GridUnit> Units;

    public static int2 Cell(float3 position) => (int2)math.floor(position.xz / CellSize);
    public static int Hash(int2 cell) => (int)math.hash(cell);

    /// <summary>The push away from overlapping neighbours, for a unit at position. Spacing: MovementConfig.SeparationSpacing.</summary>
    public static float3 Separation(in NativeParallelMultiHashMap<int, GridUnit> units, Entity self, float3 position, float radius, float spacing)
    {
        var push = float3.zero;
        var cell = Cell(position);
        for (int x = -1; x <= 1; x++)
        for (int z = -1; z <= 1; z++)
        {
            if (!units.TryGetFirstValue(Hash(cell + new int2(x, z)), out var other, out var iterator)) continue;
            do
            {
                if (other.Entity == self) continue;
                var away = position - other.Position;
                away.y = 0f;
                float minDistance = (radius + other.Radius) * spacing;
                float distanceSq = math.lengthsq(away);
                if (distanceSq >= minDistance * minDistance) continue;

                float distance = math.sqrt(distanceSq);
                // Exactly on top of each other: pick a direction from the entity index so they still split
                var direction = distance > 1e-4f ? away / distance : new float3(math.sin(self.Index), 0f, math.cos(self.Index));
                push += direction * (1f - distance / minDistance);
            }
            while (units.TryGetNextValue(out other, ref iterator));
        }
        return push;
    }

    /// <summary>The way away from enemies within radius of position, stronger the closer they are. Zero with none.</summary>
    public static float3 AwayFromEnemies(in NativeParallelMultiHashMap<int, GridUnit> units, in ComponentLookup<Enemy> enemies, float3 position, float radius)
    {
        var away = float3.zero;
        var cell = Cell(position);
        int reach = (int)math.ceil(radius / CellSize);
        for (int x = -reach; x <= reach; x++)
        for (int z = -reach; z <= reach; z++)
        {
            if (!units.TryGetFirstValue(Hash(cell + new int2(x, z)), out var other, out var iterator)) continue;
            do
            {
                if (!enemies.HasComponent(other.Entity)) continue;
                var fromEnemy = position - other.Position;
                fromEnemy.y = 0f;
                float distance = math.length(fromEnemy);
                if (distance < radius) away += math.normalizesafe(fromEnemy) * (1f - distance / radius);
            }
            while (units.TryGetNextValue(out other, ref iterator));
        }
        return away;
    }
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
