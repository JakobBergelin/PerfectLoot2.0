using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Singleton: a walkability grid in a square window around the player, with every cell's path cost to the player
/// (a flow field). Rebuilt by FlowFieldSystem, read by EnemyMovementSystem so enemies walk around walls, props, water and
/// cliffs without a navmesh. Direction gives the way to walk.
/// </summary>
public struct FlowField : IComponentData
{
    public const int Unreachable = int.MaxValue;
    public const int StraightCost = 10, DiagonalCost = 14;
    // How many path cells ahead Direction looks for the furthest one it can walk straight to (smooths the 8-way path)
    const int LookAhead = 4;

    public NativeArray<byte> Walkable;
    public NativeArray<float> Height;   // Floor height of each cell (the player's height where there's no floor)
    public NativeArray<int> Distance;   // Path cost to the player's cell (StraightCost per cell), Unreachable when cut off
    public int2 Origin;                 // World cell of the window's first cell
    public int Size;                    // Cells per side
    public float CellSize;
    public float MaxStep;               // Biggest floor height difference between neighbouring cells that units walk
    public bool HasTarget;              // Distances lead to the player (false: no player, walk straight)

    public int2 Cell(float3 position) => (int2)math.floor(position.xz / CellSize);
    public bool Contains(int2 cell) => math.all(cell >= Origin) && math.all(cell < Origin + Size);
    public int Index(int2 cell) => (cell.y - Origin.y) * Size + cell.x - Origin.x;
    public int2 CellOf(int index) => Origin + new int2(index % Size, index / Size);
    public float3 Center(int2 cell, float y) => new float3((cell.x + 0.5f) * CellSize, y, (cell.y + 0.5f) * CellSize);
    public bool IsWalkable(int2 cell) => Contains(cell) && Walkable[Index(cell)] != 0;

    /// <summary>
    /// The flat, normalized direction to walk from position toward the player at target: straight when nothing on the
    /// grid is in the way (or the field can't help: no player, outside the window, cut off), else along the path, toward
    /// the furthest of the next few path cells it can walk straight to.
    /// </summary>
    public float3 Direction(float3 position, float3 target)
    {
        var straight = math.normalizesafe(new float3(target.x - position.x, 0f, target.z - position.z));
        var cell = Cell(position);
        if (!HasTarget || !Contains(cell) || math.distancesq(position.xz, target.xz) < CellSize * CellSize * 2.25f || Clear(position, target))
            return straight;

        var waypoint = cell;
        var current = cell;
        for (int i = 0; i < LookAhead && TryNext(current, out var next); i++)
        {
            current = next;
            if (i > 0 && !Clear(position, Center(next, position.y))) break;
            waypoint = next;
        }
        if (math.all(waypoint == cell)) return straight;
        var toWaypoint = Center(waypoint, position.y) - position;
        return math.normalizesafe(new float3(toWaypoint.x, 0f, toWaypoint.z), straight);
    }

    /// <summary>The neighbouring cell one step closer to the player (lowest distance); false at the player or when cut off.</summary>
    public bool TryNext(int2 cell, out int2 next)
    {
        next = cell;
        int best = IsWalkable(cell) ? Distance[Index(cell)] : Unreachable;
        bool found = false;
        for (int x = -1; x <= 1; x++)
        for (int z = -1; z <= 1; z++)
        {
            var neighbour = cell + new int2(x, z);
            if ((x == 0 && z == 0) || !CanStep(cell, neighbour)) continue;
            int distance = Distance[Index(neighbour)];
            if (distance >= best) continue;
            best = distance;
            next = neighbour;
            found = true;
        }
        return found;
    }

    /// <summary>
    /// Whether a unit can step between neighbouring cells: onto walkable floor, not too far up or down, and diagonally only
    /// when neither corner is blocked. The cell it steps from may be blocked (a unit hugging a wall stands in one).
    /// </summary>
    public bool CanStep(int2 from, int2 to)
    {
        if (!IsWalkable(to)) return false;
        if (IsWalkable(from) && math.abs(Height[Index(from)] - Height[Index(to)]) > MaxStep) return false;
        if (from.x != to.x && from.y != to.y)
            return IsWalkable(new int2(to.x, from.y)) && IsWalkable(new int2(from.x, to.y));
        return true;
    }

    /// <summary>
    /// Whether a unit can walk the straight line from a to b: every cell the line crosses walkable (bar the first and last,
    /// which a unit hugging a wall may stand in) and no step up or down between them too big. Grid only, no physics.
    /// </summary>
    public bool Clear(float3 a, float3 b)
    {
        var start = a.xz / CellSize;
        var end = b.xz / CellSize;
        var cell = (int2)math.floor(start);
        var last = (int2)math.floor(end);
        var delta = end - start;
        var step = (int2)math.sign(delta);
        // Amanatides-Woo: how far along the line (0-1) each cell border is crossed
        var tDelta = math.select(math.abs(1f / delta), new float2(float.MaxValue), delta == 0f);
        var tMax = math.select(math.select(start - cell, cell + 1 - start, step > 0) * tDelta, new float2(float.MaxValue), step == 0);

        int previous = IsWalkable(cell) ? Index(cell) : -1;
        int steps = math.abs(last.x - cell.x) + math.abs(last.y - cell.y);
        for (int i = 0; i < steps; i++)
        {
            if (tMax.x < tMax.y) { cell.x += step.x; tMax.x += tDelta.x; }
            else { cell.y += step.y; tMax.y += tDelta.y; }
            if (!Contains(cell)) return false;
            int index = Index(cell);
            if (Walkable[index] == 0) return math.all(cell == last);
            if (previous >= 0 && math.abs(Height[index] - Height[previous]) > MaxStep) return false;
            previous = index;
        }
        return true;
    }

    public void Dispose()
    {
        if (Walkable.IsCreated) Walkable.Dispose();
        if (Height.IsCreated) Height.Dispose();
        if (Distance.IsCreated) Distance.Dispose();
    }
}

/// <summary>
/// Keeps the FlowField around the player: samples each cell's floor and clearance from physics (re-centring the window,
/// and sampling all of it, once the player is FlowRecenterDistance from the middle; otherwise a slice of the window each
/// frame, which picks up streamed chunks and new props), then searches the path cost from the player's cell when the
/// player changed cell or a cell changed. Before EnemyMovementSystem, which reads it.
/// </summary>
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateAfter(typeof(UnitSpatialGridSystem))]
[UpdateBefore(typeof(EnemyMovementSystem))]
[BurstCompile]
public partial struct FlowFieldSystem : ISystem
{
    // The whole window is sampled again over this many frames
    const int RefreshFrames = 16;

    EntityQuery _playerQuery;
    NativeArray<byte> _changed;     // Set by the sampling job when a cell's walkability changed
    int _cursor;                    // First cell of the next slice to sample
    int2 _target;                   // The player's cell at the last search
    bool _centered;

    public void OnCreate(ref SystemState state)
    {
        _playerQuery = SystemAPI.QueryBuilder().WithAll<Player, LocalTransform>().Build();
        _changed = new NativeArray<byte>(1, Allocator.Persistent);
        // Placeholders until the first update sizes them from MovementConfig (jobs need valid arrays)
        state.EntityManager.CreateSingleton(new FlowField
        {
            Walkable = new NativeArray<byte>(1, Allocator.Persistent),
            Height = new NativeArray<float>(1, Allocator.Persistent),
            Distance = new NativeArray<int>(1, Allocator.Persistent),
        });
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<MovementConfig>();
    }

    public void OnDestroy(ref SystemState state)
    {
        state.CompleteDependency();
        if (SystemAPI.TryGetSingletonRW<FlowField>(out var field)) field.ValueRW.Dispose();
        if (_changed.IsCreated) _changed.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<MovementConfig>();
        var fieldRW = SystemAPI.GetSingletonRW<FlowField>();
        // Readers from last frame (enemy movement) must be done before it's written
        state.CompleteDependency();
        ref var field = ref fieldRW.ValueRW;

        float cellSize = math.max(config.FlowCellSize, 0.25f);
        int size = math.max(8, (int)math.ceil(config.FlowWindowSize / cellSize));
        int cellCount = size * size;
        if (field.Size != size || field.CellSize != cellSize)
        {
            field.Dispose();
            field.Walkable = new NativeArray<byte>(cellCount, Allocator.Persistent);
            field.Height = new NativeArray<float>(cellCount, Allocator.Persistent);
            field.Distance = new NativeArray<int>(cellCount, Allocator.Persistent);
            field.Size = size;
            field.CellSize = cellSize;
            field.HasTarget = false;
            _centered = false;
            _cursor = 0;
        }
        // A diagonal step's worth of the steepest walkable slope, plus a step up
        field.MaxStep = config.StepHeight + cellSize * math.SQRT2 * math.tan(math.radians(math.clamp(config.MaxSlope, 0f, 85f)));

        if (_playerQuery.CalculateEntityCount() != 1)
        {
            field.HasTarget = false;
            return;
        }
        var playerPosition = SystemAPI.GetComponent<LocalTransform>(_playerQuery.GetSingletonEntity()).Position;
        var playerCell = field.Cell(playerPosition);

        bool search = !field.HasTarget || math.any(playerCell != _target);
        int start = _cursor;
        int count = (cellCount + RefreshFrames - 1) / RefreshFrames;
        int recenterCells = math.max(1, (int)(config.FlowRecenterDistance / cellSize));
        if (!_centered || math.any(math.abs(playerCell - (field.Origin + size / 2)) > recenterCells))
        {
            field.Origin = playerCell - size / 2;
            start = 0;
            count = cellCount;
            search = true;
            _centered = true;
        }
        else _cursor = (_cursor + count) % cellCount;
        field.HasTarget = true;
        _target = playerCell;

        var sampling = new SampleJob
        {
            World = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld,
            Walkable = field.Walkable,
            Height = field.Height,
            Changed = _changed,
            Origin = field.Origin,
            Size = size,
            Start = start,
            CellSize = cellSize,
            ReferenceY = playerPosition.y,
            Clearance = math.max(config.FlowClearance, 0.05f),
            MinWalkableNormalY = math.cos(math.radians(config.MaxSlope)),
        }.Schedule(count, 64, state.Dependency);

        state.Dependency = new SearchJob
        {
            Field = field,
            Changed = _changed,
            Force = search,
            Target = playerCell,
        }.Schedule(sampling);
    }

    // One cell: walkable when a ray down finds walkable ground (not water, not too steep) and no wall, prop or water is
    // within Clearance of the spot just above it
    [BurstCompile]
    struct SampleJob : IJobParallelFor
    {
        const float RayUp = 10f, RayDown = 20f;

        [ReadOnly] public CollisionWorld World;
        [NativeDisableParallelForRestriction] public NativeArray<byte> Walkable;
        [NativeDisableParallelForRestriction] public NativeArray<float> Height;
        [NativeDisableParallelForRestriction] public NativeArray<byte> Changed;
        public int2 Origin;
        public int Size, Start;
        public float CellSize, ReferenceY, Clearance, MinWalkableNormalY;

        public void Execute(int i)
        {
            int index = (Start + i) % (Size * Size);
            var xz = ((float2)(Origin + new int2(index % Size, index / Size)) + 0.5f) * CellSize;

            bool walkable = false;
            float height = ReferenceY;
            var ray = new RaycastInput
            {
                Start = new float3(xz.x, ReferenceY + RayUp, xz.y),
                End = new float3(xz.x, ReferenceY - RayDown, xz.y),
                Filter = new CollisionFilter { BelongsTo = ~0u, CollidesWith = UnitMovement.GroundLayer | (uint)CollisionLayerHelper.Water },
            };
            if (World.CastRay(ray, out var hit))
            {
                height = hit.Position.y;
                uint layer = World.Bodies[hit.RigidBodyIndex].Collider.Value.GetCollisionFilter(hit.ColliderKey).BelongsTo;
                var blockers = new CollisionFilter { BelongsTo = ~0u, CollidesWith = UnitMovement.WorldLayers | (uint)CollisionLayerHelper.Water };
                walkable = (layer & UnitMovement.GroundLayer) != 0 && hit.SurfaceNormal.y >= MinWalkableNormalY
                           && !World.CheckSphere(new float3(xz.x, height + Clearance + 0.1f, xz.y), Clearance, blockers);
            }

            byte value = (byte)(walkable ? 1 : 0);
            if (Walkable[index] != value) Changed[0] = 1;
            Walkable[index] = value;
            Height[index] = height;
        }
    }

    // Dijkstra from the player's cell over walkable cells (8 neighbours, no corner cutting), when the player changed cell or
    // a cell changed
    [BurstCompile]
    struct SearchJob : IJob
    {
        public FlowField Field;
        public NativeArray<byte> Changed;
        public bool Force;
        public int2 Target;

        public void Execute()
        {
            if (!Force && Changed[0] == 0) return;
            Changed[0] = 0;

            var distance = Field.Distance;
            for (int i = 0; i < distance.Length; i++) distance[i] = FlowField.Unreachable;
            if (!Field.Contains(Target)) return;

            // The player's own cell may be blocked (hugging a wall): it still starts the search
            var open = new NativeList<int2>(256, Allocator.Temp);   // (cost, index), a binary min-heap on cost
            int start = Field.Index(Target);
            distance[start] = 0;
            Push(ref open, new int2(0, start));
            while (open.Length > 0)
            {
                var top = Pop(ref open);
                if (top.x > distance[top.y]) continue;
                var cell = Field.CellOf(top.y);
                for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                {
                    var neighbour = cell + new int2(x, z);
                    if ((x == 0 && z == 0) || !Field.CanStep(cell, neighbour)) continue;
                    int cost = top.x + (x != 0 && z != 0 ? FlowField.DiagonalCost : FlowField.StraightCost);
                    int index = Field.Index(neighbour);
                    if (cost >= distance[index]) continue;
                    distance[index] = cost;
                    Push(ref open, new int2(cost, index));
                }
            }
        }

        static void Push(ref NativeList<int2> heap, int2 item)
        {
            heap.Add(item);
            int i = heap.Length - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (heap[parent].x <= heap[i].x) break;
                var swap = heap[parent];
                heap[parent] = heap[i];
                heap[i] = swap;
                i = parent;
            }
        }

        static int2 Pop(ref NativeList<int2> heap)
        {
            var top = heap[0];
            int last = heap.Length - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);
            int i = 0;
            while (true)
            {
                int left = i * 2 + 1, right = left + 1, smallest = i;
                if (left < heap.Length && heap[left].x < heap[smallest].x) smallest = left;
                if (right < heap.Length && heap[right].x < heap[smallest].x) smallest = right;
                if (smallest == i) break;
                var swap = heap[i];
                heap[i] = heap[smallest];
                heap[smallest] = swap;
                i = smallest;
            }
            return top;
        }
    }
}

/// <summary>
/// With Draw Flow Field on (MovementSettings): draws the flow field in the Scene view. Walkable cells get a green line
/// toward their next path cell (yellow at the player, grey when cut off), blocked cells a red cross.
/// </summary>
[UpdateInGroup(typeof(FeedbackSystemGroup))]
partial struct FlowFieldDebugSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<MovementConfig>();
        state.RequireForUpdate<FlowField>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.GetSingleton<MovementConfig>().DrawFlowField) return;
        // RW so enemy movement jobs still reading it finish first
        var field = SystemAPI.GetSingletonRW<FlowField>().ValueRO;
        state.CompleteDependency();
        if (!field.HasTarget) return;

        float mark = field.CellSize * 0.3f;
        for (int index = 0; index < field.Size * field.Size; index++)
        {
            var cell = field.CellOf(index);
            var center = field.Center(cell, field.Height[index] + 0.1f);
            if (field.Walkable[index] == 0)
            {
                Debug.DrawLine(center + new float3(-mark, 0f, -mark), center + new float3(mark, 0f, mark), Color.red);
                Debug.DrawLine(center + new float3(-mark, 0f, mark), center + new float3(mark, 0f, -mark), Color.red);
            }
            else if (field.TryNext(cell, out var next))
                Debug.DrawLine(center, math.lerp(center, field.Center(next, field.Height[field.Index(next)] + 0.1f), 0.6f), Color.green);
            else
                Debug.DrawLine(center, center + new float3(0f, 0.3f, 0f), field.Distance[index] == 0 ? Color.yellow : Color.grey);
        }
    }
}
