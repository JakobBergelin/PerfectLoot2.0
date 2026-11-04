using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// A walkability grid in a square window around one player, with every cell's path cost to that player (a flow field).
/// Rebuilt by FlowFieldSystem, read by EnemyMovementSystem so enemies walk around walls, props, water and cliffs without
/// a navmesh. Direction gives the way to walk; CanSee is line of sight (EnemyDetectionSystem).
/// </summary>
public struct FlowField : IComponentData
{
    public const int Unreachable = int.MaxValue;
    public const int StraightCost = 10, DiagonalCost = 14;
    // How many path cells ahead Direction looks for the furthest one it can walk straight to (smooths the 8-way path)
    const int LookAhead = 4;

    public NativeArray<byte> Walkable;
    public NativeArray<byte> Opaque;    // A wall or prop at eye height blocks sight through the cell (water and low props don't)
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
    public bool Clear(float3 a, float3 b) => Trace(a, b, false);

    /// <summary>
    /// Line of sight from a to b: no cell the line crosses is opaque (bar the first and last). False outside the window.
    /// Grid only, no physics.
    /// </summary>
    public bool CanSee(float3 a, float3 b) => Size > 0 && Contains(Cell(a)) && Trace(a, b, true);

    bool Trace(float3 a, float3 b, bool sight)
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
            if (sight)
            {
                if (Opaque[index] != 0 && !math.all(cell == last)) return false;
                continue;
            }
            if (Walkable[index] == 0) return math.all(cell == last);
            if (previous >= 0 && math.abs(Height[index] - Height[previous]) > MaxStep) return false;
            previous = index;
        }
        return true;
    }

    public void Dispose()
    {
        if (Walkable.IsCreated) Walkable.Dispose();
        if (Opaque.IsCreated) Opaque.Dispose();
        if (Height.IsCreated) Height.Dispose();
        if (Distance.IsCreated) Distance.Dispose();
    }
}

/// <summary>
/// Singleton: one FlowField per player, by EnemyTargetable.FieldSlot, so players can be far apart. Plain fields rather than
/// a list so jobs can read them.
/// </summary>
public struct FlowFields : IComponentData
{
    public const int MaxFields = 4;

    public FlowField Field0, Field1, Field2, Field3;

    public FlowField this[int slot]
    {
        get => slot switch { 1 => Field1, 2 => Field2, 3 => Field3, _ => Field0 };
        set
        {
            switch (slot)
            {
                case 1: Field1 = value; break;
                case 2: Field2 = value; break;
                case 3: Field3 = value; break;
                default: Field0 = value; break;
            }
        }
    }
}

/// <summary>
/// Keeps each player's FlowField around it: samples each cell's floor and clearance from physics (re-centring the window,
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

    /// <summary>How far one field's sampling and search have come.</summary>
    struct FieldProgress
    {
        public int Cursor;      // First cell of the next slice to sample
        public int2 Target;     // The player's cell at the last search
        public bool Centered;
    }

    NativeArray<byte> _changed;             // Per field: set by the sampling job when a cell's walkability changed
    NativeArray<FieldProgress> _progress;   // Per field

    public void OnCreate(ref SystemState state)
    {
        _changed = new NativeArray<byte>(FlowFields.MaxFields, Allocator.Persistent);
        _progress = new NativeArray<FieldProgress>(FlowFields.MaxFields, Allocator.Persistent);
        // Placeholders until a field's first update sizes it from MovementConfig (jobs need valid arrays)
        var fields = new FlowFields();
        for (int slot = 0; slot < FlowFields.MaxFields; slot++)
        {
            fields[slot] = new FlowField
            {
                Walkable = new NativeArray<byte>(1, Allocator.Persistent),
                Opaque = new NativeArray<byte>(1, Allocator.Persistent),
                Height = new NativeArray<float>(1, Allocator.Persistent),
                Distance = new NativeArray<int>(1, Allocator.Persistent),
            };
        }
        state.EntityManager.CreateSingleton(fields);
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<MovementConfig>();
    }

    public void OnDestroy(ref SystemState state)
    {
        state.CompleteDependency();
        if (SystemAPI.TryGetSingleton<FlowFields>(out var fields))
            for (int slot = 0; slot < FlowFields.MaxFields; slot++) fields[slot].Dispose();
        if (_changed.IsCreated) _changed.Dispose();
        if (_progress.IsCreated) _progress.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<MovementConfig>();
        var fieldsRW = SystemAPI.GetSingletonRW<FlowFields>();
        // Readers from last frame (enemy movement) must be done before they're written
        state.CompleteDependency();
        ref var fields = ref fieldsRW.ValueRW;
        var world = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;

        int used = 0;
        foreach (var (targetable, transform) in SystemAPI.Query<RefRO<EnemyTargetable>, RefRO<LocalTransform>>().WithAll<Player>())
        {
            int slot = targetable.ValueRO.FieldSlot;
            if (slot < 0 || slot >= FlowFields.MaxFields) continue;
            var field = fields[slot];
            UpdateField(ref state, ref field, slot, transform.ValueRO.Position, config, world);
            fields[slot] = field;
            used |= 1 << slot;
        }

        // A field without a player leads nowhere
        for (int slot = 0; slot < FlowFields.MaxFields; slot++)
        {
            if ((used & (1 << slot)) != 0) continue;
            var field = fields[slot];
            field.HasTarget = false;
            fields[slot] = field;
        }
    }

    void UpdateField(ref SystemState state, ref FlowField field, int slot, float3 playerPosition, in MovementConfig config, in CollisionWorld world)
    {
        var progress = _progress[slot];
        float cellSize = math.max(config.FlowCellSize, 0.25f);
        int size = math.max(8, (int)math.ceil(config.FlowWindowSize / cellSize));
        int cellCount = size * size;
        if (field.Size != size || field.CellSize != cellSize)
        {
            field.Dispose();
            field.Walkable = new NativeArray<byte>(cellCount, Allocator.Persistent);
            field.Opaque = new NativeArray<byte>(cellCount, Allocator.Persistent);
            field.Height = new NativeArray<float>(cellCount, Allocator.Persistent);
            field.Distance = new NativeArray<int>(cellCount, Allocator.Persistent);
            field.Size = size;
            field.CellSize = cellSize;
            field.HasTarget = false;
            progress.Centered = false;
            progress.Cursor = 0;
        }
        // A diagonal step's worth of the steepest walkable slope, plus a step up
        field.MaxStep = config.StepHeight + cellSize * math.SQRT2 * math.tan(math.radians(math.clamp(config.MaxSlope, 0f, 85f)));

        var playerCell = field.Cell(playerPosition);

        bool search = !field.HasTarget || math.any(playerCell != progress.Target);
        int start = progress.Cursor;
        int count = (cellCount + RefreshFrames - 1) / RefreshFrames;
        int recenterCells = math.max(1, (int)(config.FlowRecenterDistance / cellSize));
        if (!progress.Centered || math.any(math.abs(playerCell - (field.Origin + size / 2)) > recenterCells))
        {
            field.Origin = playerCell - size / 2;
            start = 0;
            count = cellCount;
            search = true;
            progress.Centered = true;
        }
        else progress.Cursor = (progress.Cursor + count) % cellCount;
        field.HasTarget = true;
        progress.Target = playerCell;
        _progress[slot] = progress;

        var changed = _changed.GetSubArray(slot, 1);

        var sampling = new SampleJob
        {
            World = world,
            Walkable = field.Walkable,
            Opaque = field.Opaque,
            Height = field.Height,
            Changed = changed,
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
            Changed = changed,
            Force = search,
            Target = playerCell,
        }.Schedule(sampling);
    }

    // One cell: walkable when a ray down finds walkable ground (not water, not too steep) and no wall, prop or water is
    // within Clearance of the spot just above it; opaque when a wall or prop reaches into it at eye height
    [BurstCompile]
    struct SampleJob : IJobParallelFor
    {
        const float RayUp = 10f, RayDown = 20f;
        const float EyeHeight = 1.2f;

        [ReadOnly] public CollisionWorld World;
        [NativeDisableParallelForRestriction] public NativeArray<byte> Walkable;
        [NativeDisableParallelForRestriction] public NativeArray<byte> Opaque;
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
            var sightBlockers = new CollisionFilter { BelongsTo = ~0u, CollidesWith = UnitMovement.WorldLayers };
            bool opaque = World.CheckSphere(new float3(xz.x, height + EyeHeight, xz.y), CellSize * 0.5f, sightBlockers);

            byte value = (byte)(walkable ? 1 : 0);
            if (Walkable[index] != value) Changed[0] = 1;
            Walkable[index] = value;
            Opaque[index] = (byte)(opaque ? 1 : 0);
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
        state.RequireForUpdate<FlowFields>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.GetSingleton<MovementConfig>().DrawFlowField) return;
        // RW so enemy movement jobs still reading them finish first
        var fields = SystemAPI.GetSingletonRW<FlowFields>().ValueRO;
        state.CompleteDependency();
        for (int slot = 0; slot < FlowFields.MaxFields; slot++)
            if (fields[slot].HasTarget) Draw(fields[slot]);
    }

    static void Draw(in FlowField field)
    {
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
