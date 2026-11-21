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
        public ulong EdgeMask;      // Edge props: the edge connections (bits by index) to place along
        public bool AlongWalkableBorder;    // Edge props without edge connections: where walkable ground meets anything else
    }

    /// <summary>A placed thing: Grid is in cells (x across, y forward) on the unbent grid, Height the ground's height there.</summary>
    public struct PlacementPoint
    {
        public int Rule;
        public float2 Grid;
        public float Yaw;           // Radians
        public float Height;        // Meters above the bent grid
    }

    public static class LevelLooks
    {
        /// <summary>
        /// Which look (0 is the tile's Prefab, then its Looks) each cell shows: random by seed, but never the one of the cell
        /// west or south of it when that cell has the same tile (the cells east and north avoid this one in turn), as long as
        /// the tile has looks enough. lookCounts holds each tile's number of looks.
        /// </summary>
        public static int[] Pick(int width, int height, int[] packed, int[] lookCounts, uint seed)
        {
            var looks = new int[packed.Length];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int cell = x + y * width;
                    if (packed[cell] < 0) continue;
                    int tile = LevelLayoutAuthoring.PackedTile(packed[cell]);
                    int count = tile < lookCounts.Length ? lookCounts[tile] : 1;
                    if (count <= 1) continue;
                    int west = x > 0 && packed[cell - 1] >= 0 && LevelLayoutAuthoring.PackedTile(packed[cell - 1]) == tile ? looks[cell - 1] : -1;
                    int south = y > 0 && packed[cell - width] >= 0 && LevelLayoutAuthoring.PackedTile(packed[cell - width]) == tile ? looks[cell - width] : -1;
                    var random = new Random(math.max(1u, math.hash(new uint3(seed, (uint)cell, 0x100Cu))));
                    int options = count - (west >= 0 ? 1 : 0) - (south >= 0 && south != west ? 1 : 0);
                    if (options <= 0)
                    {
                        looks[cell] = random.NextInt(count);
                        continue;
                    }
                    int pick = random.NextInt(options);
                    for (int look = 0; look < count; look++)
                    {
                        if (look == west || look == south) continue;
                        if (pick-- == 0) { looks[cell] = look; break; }
                    }
                }
            return looks;
        }
    }

    /// <summary>
    /// The finished layout for gameplay: each corner's terrain (WfcTerrain, or Unknown) and ground height, and whether each
    /// edge can be walked across. Read from each cell's tile (Tile * 4 + Rotation) and, under pieces, from the
    /// single-connection corner masks. Edges are indexed like the solver's: Width * (Height + 1) running east-west, then
    /// (Width + 1) * Height running north-south; an edge no tile names (beside a piece) can be walked across.
    /// </summary>
    public class LevelSurface
    {
        public const byte Unknown = 255;

        public byte[] Terrain;
        public float[] Height;
        public byte[] EdgeWalkable;
        /// <summary>Each corner's and edge's connection index, or Unknown.</summary>
        public int[] CornerConnection, EdgeConnection;

        /// <param name="tileCorners">4 corner connection indices per tile (SW, SE, NE, NW), Unknown where missing</param>
        /// <param name="tileEdges">4 edge connection indices per tile (S, E, N, W), Unknown where missing</param>
        /// <param name="tileWalkThrough">1 per Walk Through tile: its 4 edges count as walkable (units walk through it, a ramp)</param>
        public static LevelSurface Build(int width, int height, int[] packedCells, byte[] tileCorners, byte[] tileEdges, byte[] tileWalkThrough, ulong[] cornerMasks,
                                         byte[] connectionTerrains, float[] connectionHeights, byte[] edgeWalkables)
        {
            int cornerWidth = width + 1, across = width * (height + 1);
            var corners = new int[cornerWidth * (height + 1)];
            var edges = new int[across + cornerWidth * height];
            for (int c = 0; c < corners.Length; c++)
            {
                ulong mask = c < cornerMasks.Length ? cornerMasks[c] : 0ul;
                corners[c] = mask != 0ul && (mask & (mask - 1)) == 0ul ? math.tzcnt(mask) : Unknown;
            }
            for (int e = 0; e < edges.Length; e++) edges[e] = Unknown;

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int packed = packedCells[x + y * width];
                    if (packed < 0) continue;
                    int tile = LevelLayoutAuthoring.PackedTile(packed), rotation = LevelLayoutAuthoring.PackedRotation(packed);
                    bool mirror = LevelLayoutAuthoring.PackedMirror(packed);
                    if (tile * 4 + 3 >= tileCorners.Length || tile * 4 + 3 >= tileEdges.Length) continue;
                    // What the placed (mirrored, turned) tile has at each corner and edge (WfcRotation.Source)
                    int Corner(int k) => tileCorners[tile * 4 + WfcRotation.Source(k, rotation, mirror, false)];
                    int Edge(int k) => tileEdges[tile * 4 + WfcRotation.Source(k, rotation, mirror, true)];
                    corners[x + y * cornerWidth] = Corner(0);
                    corners[x + 1 + y * cornerWidth] = Corner(1);
                    corners[x + 1 + (y + 1) * cornerWidth] = Corner(2);
                    corners[x + (y + 1) * cornerWidth] = Corner(3);
                    edges[x + y * width] = Edge(0);
                    edges[across + x + 1 + y * cornerWidth] = Edge(1);
                    edges[x + (y + 1) * width] = Edge(2);
                    edges[across + x + y * cornerWidth] = Edge(3);
                }

            var surface = new LevelSurface
            {
                Terrain = new byte[corners.Length], Height = new float[corners.Length], EdgeWalkable = new byte[edges.Length],
                CornerConnection = corners, EdgeConnection = edges,
            };
            for (int c = 0; c < corners.Length; c++)
            {
                bool known = corners[c] < connectionTerrains.Length;
                surface.Terrain[c] = known ? connectionTerrains[corners[c]] : Unknown;
                surface.Height[c] = known ? connectionHeights[corners[c]] : 0f;
            }
            for (int e = 0; e < edges.Length; e++)
                surface.EdgeWalkable[e] = edges[e] < edgeWalkables.Length ? edgeWalkables[edges[e]] : (byte)1;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int packed = packedCells[x + y * width];
                    if (packed < 0) continue;
                    int tile = LevelLayoutAuthoring.PackedTile(packed);
                    if (tile >= tileWalkThrough.Length || tileWalkThrough[tile] == 0) continue;
                    surface.EdgeWalkable[x + y * width] = surface.EdgeWalkable[x + (y + 1) * width] = 1;
                    surface.EdgeWalkable[across + x + y * cornerWidth] = surface.EdgeWalkable[across + x + 1 + y * cornerWidth] = 1;
                }
            return surface;
        }
    }

    /// <summary>
    /// Fills the level with placements, rule by rule: points spread at least the rule's Spacing apart (Bridson's
    /// Poisson-disk sampling, restarted in every region it hasn't reached) on the floor reachable from the start, kept
    /// only in cells whose 4 corners are at one height (not on ramps or cliffs), where nothing unwalkable or water is
    /// within ClearRadius, not on a piece, at least MinDistanceFromStart from the start, outside every hand-placed
    /// prefab's reserved radius, and for spawners on ground no steeper than MaxSlope and Spacing away from every other
    /// spawner. Deterministic per seed; used by the editor preview and at runtime.
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
        // LevelSurface
        [ReadOnly] public NativeArray<byte> CornerTerrain;
        [ReadOnly] public NativeArray<float> CornerHeight;
        [ReadOnly] public NativeArray<byte> EdgeWalkable;
        [ReadOnly] public NativeArray<int> EdgeConnection;  // Edge connection index per edge (LevelSurface), for edge props
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
                if (rule.Kind == PlacementKind.EdgeProp)
                {
                    PlaceAlongEdges(r, rule, spacing, reachable);
                    continue;
                }
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
            int2 cell = (int2)math.floor(point);
            Points.Add(new PlacementPoint { Rule = ruleIndex, Grid = point, Yaw = random.NextFloat(2f * math.PI), Height = CornerHeight[cell.x + cell.y * (Width + 1)] });
            return true;
        }

        bool IsValid(float2 point, PlacementRuleData rule, NativeArray<bool> reachable)
        {
            int2 cell = (int2)math.floor(point);
            if (OccupiedCells[cell.x + cell.y * Width]) return false;
            // Flat cells only: a ramp or cliff has corners at different heights
            int stride = Width + 1, southWest = cell.x + cell.y * stride;
            float ground = CornerHeight[southWest];
            if (CornerHeight[southWest + 1] != ground || CornerHeight[southWest + stride] != ground || CornerHeight[southWest + stride + 1] != ground)
                return false;

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

        // Edge props: in the middle of grid edges (between two corners), along the rule's edge connections or, without any,
        // where walkable ground meets anything else. Each faces its walkable (else its lower) side and stands at that side's
        // height; the walkable side must be reachable from the start. Spacing apart, in random order.
        void PlaceAlongEdges(int ruleIndex, PlacementRuleData rule, float spacing, NativeArray<bool> reachable)
        {
            var random = new Random(math.max(1u, math.hash(new uint2(Seed, (uint)ruleIndex + 1u))));
            int cornerWidth = Width + 1, across = Width * (Height + 1), edges = across + cornerWidth * Height;
            var order = new NativeArray<int>(edges, Allocator.Temp);
            for (int e = 0; e < edges; e++) order[e] = e;
            for (int i = edges - 1; i > 0; i--)
            {
                int j = random.NextInt(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            var own = new NativeParallelMultiHashMap<int2, float2>(64, Allocator.Temp);
            byte walkable = (byte)WfcTerrain.Walkable;

            for (int o = 0; o < edges; o++)
            {
                int e = order[o];
                // The edge's corners a and b, and the cells on either side of it
                bool alongX = e < across;
                int ex = alongX ? e % Width : (e - across) % cornerWidth, ey = alongX ? e / Width : (e - across) / cornerWidth;
                int a = ex + ey * cornerWidth, b = alongX ? a + 1 : a + cornerWidth;
                int2 sideA = alongX ? new int2(ex, ey - 1) : new int2(ex - 1, ey), sideB = new int2(ex, ey);

                bool walkA = CornerTerrain[a] == walkable, walkB = CornerTerrain[b] == walkable;
                bool along = !rule.AlongWalkableBorder ? EdgeConnection[e] < WfcConnections.Max && ((rule.EdgeMask >> EdgeConnection[e]) & 1ul) != 0ul
                                                  : walkA != walkB;
                if (!along || !(walkA && reachable[a] || walkB && reachable[b])) continue;
                if (Occupied(sideA) || Occupied(sideB)) continue;

                int toward = walkA != walkB ? (walkA ? a : b) : CornerHeight[a] < CornerHeight[b] ? a : b;
                int from = toward == a ? b : a;
                var towardPoint = new float2(toward % cornerWidth, toward / cornerWidth);
                var fromPoint = new float2(from % cornerWidth, from / cornerWidth);
                var middle = (towardPoint + fromPoint) * 0.5f;
                if (math.distance(middle, Start) * TileSize < rule.MinDistanceFromStart) continue;
                bool reservedNear = false;
                for (int i = 0; i < Reserved.Length; i++)
                    reservedNear |= math.distance(middle, Reserved[i].xy) * TileSize < Reserved[i].z;
                if (reservedNear || TooClose(own, spacing, middle, spacing)) continue;

                own.Add(Bucket(middle, spacing), middle);
                var facing = towardPoint - fromPoint;
                Points.Add(new PlacementPoint { Rule = ruleIndex, Grid = middle, Yaw = math.atan2(facing.x, facing.y), Height = CornerHeight[toward] });
            }
        }

        bool Occupied(int2 cell) => cell.x >= 0 && cell.y >= 0 && cell.x < Width && cell.y < Height && OccupiedCells[cell.x + cell.y * Width];

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

        // Walkable corners reachable from the start (neighbors across an edge that can be walked across, not diagonals);
        // everything walkable when the start isn't
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
                    // The edge from this corner to the next: east-west edges first, then north-south ones
                    int edge = n == 0 ? x + y * Width : n == 2 ? x - 1 + y * Width
                             : Width * (Height + 1) + x + (n == 1 ? y : y - 1) * cornerWidth;
                    if (reachable[next] || CornerTerrain[next] != walkable || EdgeWalkable[edge] == 0) continue;
                    reachable[next] = true;
                    queue.Add(next);
                }
            }
            return reachable;
        }
    }
}
