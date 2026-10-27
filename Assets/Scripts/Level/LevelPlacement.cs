using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace LevelGeneration
{
    /// <summary>One placement rule as the job sees it (from a LevelPlacementSet rule).</summary>
    public struct PlacementRuleData
    {
        public PlacementKind Kind;
        public float Spacing, ClearRadius, MinDistanceFromStart;
        public float MaxSlope;      // Degrees (spawners)
    }

    /// <summary>A placed thing: Grid is in cells (x across, y forward) on the unbent grid.</summary>
    public struct PlacementPoint
    {
        public int Rule;
        public float2 Grid;
        public float Yaw;           // Radians
    }

    public static class LevelPlacement
    {
        public const byte Unknown = 255;

        /// <summary>
        /// Every corner's terrain (WfcTerrain, or Unknown) in the finished layout: from each cell's tile (Tile * 4 + Rotation,
        /// tileCorners holds 4 per tile: SW, SE, NE, NW) and, under pieces, from the single-terrain corner masks.
        /// </summary>
        public static byte[] CornerTerrain(int width, int height, int[] packedCells, byte[] tileCorners, byte[] cornerMasks)
        {
            int cornerWidth = width + 1;
            var terrain = new byte[cornerWidth * (height + 1)];
            for (int c = 0; c < terrain.Length; c++)
            {
                byte mask = c < cornerMasks.Length ? cornerMasks[c] : (byte)0;
                terrain[c] = mask != 0 && (mask & (mask - 1)) == 0 ? (byte)math.tzcnt((int)mask) : Unknown;
            }
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int packed = packedCells[x + y * width];
                    if (packed < 0) continue;
                    int tile = LevelLayoutAuthoring.PackedTile(packed), rotation = LevelLayoutAuthoring.PackedRotation(packed);
                    if (tile * 4 + 3 >= tileCorners.Length) continue;
                    for (int k = 0; k < 4; k++)
                    {
                        int cx = x + (k == 1 || k == 2 ? 1 : 0), cy = y + (k >= 2 ? 1 : 0);
                        // A tile turned r times has at corner k what it has at (k + r) % 4
                        terrain[cx + cy * cornerWidth] = tileCorners[tile * 4 + (k + rotation) % 4];
                    }
                }
            return terrain;
        }
    }

    /// <summary>
    /// Fills the level with placements, rule by rule: points spread at least the rule's Spacing apart (Bridson's
    /// Poisson-disk sampling, restarted in every region it hasn't reached) on the floor reachable from the start, kept
    /// only where no wall or water is within ClearRadius, not on a piece, at least MinDistanceFromStart from the start,
    /// outside every hand-placed prefab's reserved radius,
    /// and for spawners on ground no steeper than MaxSlope and Spacing away from every other spawner. Deterministic per
    /// seed; used by the editor preview and at runtime.
    /// </summary>
    [BurstCompile]
    public struct LevelPlacementJob : IJob
    {
        const int Candidates = 30;

        public int Width, Height;
        public float TileSize;
        public uint Seed;
        public int StartCorner;
        public float2 Start;    // Player start, in cells
        [ReadOnly] public NativeArray<byte> CornerTerrain;
        [ReadOnly] public NativeArray<bool> OccupiedCells;
        [ReadOnly] public NativeArray<float3> BentCorners;
        [ReadOnly] public NativeArray<PlacementRuleData> Rules;
        /// <summary>Kept clear around hand-placed prefabs: x, y the center in cells, z the radius in meters.</summary>
        [ReadOnly] public NativeArray<float3> Reserved;
        public NativeList<PlacementPoint> Points;

        public void Execute()
        {
            Points.Clear();
            if (Width <= 0 || Height <= 0 || TileSize <= 0f) return;
            var reachable = FloodReachable();

            // Spawners keep away from each other across rules: one shared lookup, buckets as big as the largest spacing
            float spawnerBucket = 1f;
            for (int r = 0; r < Rules.Length; r++)
                if (Rules[r].Kind == PlacementKind.EnemySpawner) spawnerBucket = math.max(spawnerBucket, Rules[r].Spacing / TileSize);
            var spawners = new NativeParallelMultiHashMap<int2, float2>(64, Allocator.Temp);

            for (int r = 0; r < Rules.Length; r++)
            {
                var rule = Rules[r];
                float spacing = rule.Spacing / TileSize;
                if (spacing <= 0f) continue;
                bool isSpawner = rule.Kind == PlacementKind.EnemySpawner;
                var own = new NativeParallelMultiHashMap<int2, float2>(64, Allocator.Temp);
                float ownBucket = spacing;
                var random = new Random(math.max(1u, math.hash(new uint2(Seed, (uint)r + 1u))));
                var active = new NativeList<float2>(Allocator.Temp);

                // Seed tries over the whole grid (a jittered grid in random order), so separate regions are filled too
                var seeds = new NativeList<float2>(Allocator.Temp);
                for (float y = 0f; y < Height; y += spacing)
                    for (float x = 0f; x < Width; x += spacing)
                        seeds.Add(new float2(x + random.NextFloat(spacing), y + random.NextFloat(spacing)));
                for (int i = seeds.Length - 1; i > 0; i--)
                {
                    int j = random.NextInt(i + 1);
                    (seeds[i], seeds[j]) = (seeds[j], seeds[i]);
                }

                foreach (var seed in seeds)
                {
                    if (!TryAdd(seed, r, rule, spacing, isSpawner, ref own, ownBucket, ref spawners, spawnerBucket, ref random, reachable)) continue;
                    active.Add(seed);
                    while (active.Length > 0)
                    {
                        int index = random.NextInt(active.Length);
                        var from = active[index];
                        bool added = false;
                        for (int k = 0; k < Candidates && !added; k++)
                        {
                            float angle = random.NextFloat(2f * math.PI);
                            float distance = spacing * (1f + random.NextFloat());
                            var candidate = from + new float2(math.cos(angle), math.sin(angle)) * distance;
                            if (TryAdd(candidate, r, rule, spacing, isSpawner, ref own, ownBucket, ref spawners, spawnerBucket, ref random, reachable))
                            {
                                active.Add(candidate);
                                added = true;
                            }
                        }
                        if (!added) active.RemoveAtSwapBack(index);
                    }
                }
            }
        }

        bool TryAdd(float2 point, int ruleIndex, PlacementRuleData rule, float spacing, bool isSpawner,
                    ref NativeParallelMultiHashMap<int2, float2> own, float ownBucket,
                    ref NativeParallelMultiHashMap<int2, float2> spawners, float spawnerBucket, ref Random random, NativeArray<bool> reachable)
        {
            if (point.x < 0f || point.y < 0f || point.x >= Width || point.y >= Height) return false;
            if (!IsValid(point, rule, reachable)) return false;
            if (isSpawner ? TooClose(spawners, spawnerBucket, point, spacing) : TooClose(own, ownBucket, point, spacing)) return false;

            if (isSpawner) spawners.Add(Bucket(point, spawnerBucket), point);
            else own.Add(Bucket(point, ownBucket), point);
            Points.Add(new PlacementPoint { Rule = ruleIndex, Grid = point, Yaw = random.NextFloat(2f * math.PI) });
            return true;
        }

        bool IsValid(float2 point, PlacementRuleData rule, NativeArray<bool> reachable)
        {
            int2 cell = (int2)math.floor(point);
            if (OccupiedCells[cell.x + cell.y * Width]) return false;

            // On reachable floor: the nearest corner's quarter is where the point stands
            int2 nearest = (int2)math.round(point);
            if (nearest.x < 0 || nearest.y < 0 || nearest.x > Width || nearest.y > Height || !reachable[nearest.x + nearest.y * (Width + 1)]) return false;

            // No wall or water within the clear radius
            float clear = rule.ClearRadius / TileSize;
            int reach = (int)math.ceil(clear);
            for (int y = nearest.y - reach - 1; y <= nearest.y + reach + 1; y++)
                for (int x = nearest.x - reach - 1; x <= nearest.x + reach + 1; x++)
                {
                    if (x < 0 || y < 0 || x > Width || y > Height) continue;
                    // A corner's terrain covers half a cell around it: blocked when that square comes within the radius
                    var nearestInSquare = math.clamp(point, new float2(x - 0.5f, y - 0.5f), new float2(x + 0.5f, y + 0.5f));
                    if (math.distance(point, nearestInSquare) > clear) continue;
                    if (CornerTerrain[x + y * (Width + 1)] != (byte)WfcTerrain.Walkable) return false;
                }

            if (math.distance(point, Start) * TileSize < rule.MinDistanceFromStart) return false;
            for (int i = 0; i < Reserved.Length; i++)
                if (math.distance(point, Reserved[i].xy) * TileSize < Reserved[i].z) return false;
            if (rule.Kind == PlacementKind.EnemySpawner && Slope(point) > rule.MaxSlope) return false;
            return true;
        }

        // Degrees between the bent ground's normal and up, at the point
        float Slope(float2 point)
        {
            int cx = math.clamp((int)math.floor(point.x), 0, Width - 1), cy = math.clamp((int)math.floor(point.y), 0, Height - 1);
            float fx = point.x - cx, fy = point.y - cy;
            int stride = Width + 1;
            var c00 = BentCorners[cx + cy * stride];
            var c10 = BentCorners[cx + 1 + cy * stride];
            var c01 = BentCorners[cx + (cy + 1) * stride];
            var c11 = BentCorners[cx + 1 + (cy + 1) * stride];
            var alongX = math.lerp(c10 - c00, c11 - c01, fy);
            var alongY = math.lerp(c01 - c00, c11 - c10, fx);
            var normal = math.normalizesafe(math.cross(alongY, alongX), new float3(0f, 1f, 0f));
            return math.degrees(math.acos(math.clamp(math.abs(normal.y), 0f, 1f)));
        }

        static int2 Bucket(float2 point, float size) => (int2)math.floor(point / size);

        static bool TooClose(NativeParallelMultiHashMap<int2, float2> map, float bucketSize, float2 point, float spacing)
        {
            var bucket = Bucket(point, bucketSize);
            int reach = (int)math.ceil(spacing / bucketSize);
            for (int y = -reach; y <= reach; y++)
                for (int x = -reach; x <= reach; x++)
                {
                    if (!map.TryGetFirstValue(bucket + new int2(x, y), out var other, out var iterator)) continue;
                    do
                    {
                        if (math.distancesq(other, point) < spacing * spacing) return true;
                    } while (map.TryGetNextValue(out other, ref iterator));
                }
            return false;
        }

        // Walkable corners reachable from the start (neighbors, not diagonals); everything walkable when the start isn't
        NativeArray<bool> FloodReachable()
        {
            int corners = (Width + 1) * (Height + 1);
            var reachable = new NativeArray<bool>(corners, Allocator.Temp);
            byte walkable = (byte)WfcTerrain.Walkable;
            if (StartCorner < 0 || StartCorner >= corners || CornerTerrain[StartCorner] != walkable)
            {
                for (int c = 0; c < corners; c++) reachable[c] = CornerTerrain[c] == walkable;
                return reachable;
            }
            var queue = new NativeList<int>(corners, Allocator.Temp);
            queue.Add(StartCorner);
            reachable[StartCorner] = true;
            int cornerWidth = Width + 1;
            for (int head = 0; head < queue.Length; head++)
            {
                int corner = queue[head];
                int x = corner % cornerWidth, y = corner / cornerWidth;
                for (int n = 0; n < 4; n++)
                {
                    int nx = x + (n == 0 ? 1 : n == 2 ? -1 : 0), ny = y + (n == 1 ? 1 : n == 3 ? -1 : 0);
                    if (nx < 0 || ny < 0 || nx > Width || ny > Height) continue;
                    int next = nx + ny * cornerWidth;
                    if (reachable[next] || CornerTerrain[next] != walkable) continue;
                    reachable[next] = true;
                    queue.Add(next);
                }
            }
            return reachable;
        }
    }
}
