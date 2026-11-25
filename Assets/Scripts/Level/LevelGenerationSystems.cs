using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace LevelGeneration
{
    /// <summary>
    /// Builds baked level layouts: solves the whole level with WfcSolveJob (or takes the saved layout) and bends the grid
    /// (LevelDeformation, seeded like the solve), then streams the geometry in chunks of LevelChunkBuilder.ChunkSize cells (built with Burst jobs that finish on later frames):
    /// the chunks within LoadDistance of the player start before the level is ready, the rest as any player comes within
    /// LoadDistance (nearest first, at most ChunksPerFrame per frame). Built chunks stay. LevelReady is announced a frame
    /// after the first chunks. Frees the meshes and colliders it made when destroyed.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class LevelGenerationSystem : SystemBase
    {
        // A solved level whose chunks are built on demand
        class LevelStream
        {
            public Entity Layout;
            public int[] Packed;            // Per cell: Tile * 4 + Rotation, or negative for nothing
            public float3[] Corners;        // Bent grid (where tile models are bent to)
            public float3[] Ground;         // The bent grid at each corner's ground height (where things stand)
            // The background ring: Margin cells more on every side; Ring holds its cells (Tile * 4 + Rotation) on that
            // bigger grid, or is null. Chunks and JobCorners (the bent grid for the chunk jobs) cover the bigger grid.
            public int Margin;
            public int[] Ring;
            public int[] Looks, RingLooks;   // Per cell (of Packed, of Ring): which look of its tile it shows (LevelLooks)
            public NativeArray<float3> JobCorners;
            public NativeArray<float2> JobFlow;     // Which way the water flows at each corner of the bigger grid (LevelFlow)
            public Material[] Materials;
            public Entity[] PropPrefabs;
            public int ChunksX, ChunksY;
            public bool[] Built;
            public List<LevelPiecePlacement> Pieces; // Hand-placed pieces, then this seed's landmarks
            public List<int>[] PiecesByChunk; // Piece placements, by the chunk of their south-west cell
            public List<PlacementPoint>[] PlacedPropsByChunk;   // Procedural props, spawned with their chunk
            public Entity[] PlacementPrefabs;
            public float LoadDistance;
            public int ChunksPerFrame;
        }

        // A chunk whose jobs are running; finished (meshes and entities made) on a later frame once they're done
        class PendingChunk
        {
            public LevelStream Stream;
            public LevelChunkBuilder Builder;
            public int Chunk;
        }

        readonly List<LevelStream> _streams = new List<LevelStream>();
        readonly List<PendingChunk> _pending = new List<PendingChunk>();
        readonly List<Mesh> _meshes = new List<Mesh>();
        readonly List<BlobAssetReference<Unity.Physics.Collider>> _colliders = new List<BlobAssetReference<Unity.Physics.Collider>>();

        protected override void OnCreate()
        {
            RequireForUpdate<LevelLayout>();
        }

        // The level is gone (no LevelLayout left): so is its abyss fade
        protected override void OnStopRunning()
        {
            ClearBackdrop();
            ClearForestMask();
        }

        static void DisposeJobData(LevelStream stream)
        {
            if (stream.JobCorners.IsCreated) stream.JobCorners.Dispose();
            if (stream.JobFlow.IsCreated) stream.JobFlow.Dispose();
        }

        protected override void OnDestroy()
        {
            foreach (var pending in _pending) pending.Builder.Dispose();
            foreach (var stream in _streams) DisposeJobData(stream);
            foreach (var collider in _colliders) if (collider.IsCreated) collider.Dispose();
            foreach (var mesh in _meshes) if (mesh != null) Object.Destroy(mesh);
            ClearBackdrop();
            ClearForestMask();
            _pending.Clear();
            _streams.Clear();
            _colliders.Clear();
            _meshes.Clear();
        }

        protected override void OnUpdate()
        {
            // Built last frame: transforms are calculated now, so the level is ready
            bool announce = false;
            var ready = new LevelReady();
            foreach (var spawned in SystemAPI.Query<RefRW<LevelSpawned>>())
            {
                if (spawned.ValueRO.ReadyAnnounced) continue;
                spawned.ValueRW.ReadyAnnounced = true;
                announce = true;
                ready.PlayerStart = spawned.ValueRO.PlayerStart;
            }
            if (announce && !SystemAPI.HasSingleton<LevelReady>()) EntityManager.CreateSingleton(ready, "LevelReady");

            using var pending = SystemAPI.QueryBuilder().WithAll<LevelLayout>().WithNone<LevelSpawned>().Build().ToEntityArray(Allocator.Temp);
            foreach (var entity in pending)
            {
                var playerStart = Build(entity);
                EntityManager.AddComponentData(entity, new LevelSpawned { PlayerStart = playerStart });
            }

            // Finish chunks whose jobs are done (started on earlier frames, so the main thread never waits for them)
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var started = _pending[i];
                if (!started.Builder.Handle.IsCompleted) continue;
                _pending.RemoveAt(i);
                if (IsAlive(started.Stream)) Finish(started);
                else started.Builder.Dispose();
            }

            // Stream chunks around every machine's players, who can be far apart (before they spawn, the start's chunks are
            // already built)
            if (_streams.Count == 0) return;
            var players = new NativeList<float3>(Allocator.Temp);
            foreach (var transform in SystemAPI.Query<RefRO<LocalTransform>>().WithAny<NetPlayer, Player>())
                players.Add(transform.ValueRO.Position);
            if (players.IsEmpty) return;

            for (int i = _streams.Count - 1; i >= 0; i--)
            {
                var stream = _streams[i];
                if (!IsAlive(stream))
                {
                    if (!_pending.Exists(p => p.Stream == stream)) DisposeJobData(stream);
                    _streams.RemoveAt(i);
                    continue;
                }
                int budget = stream.ChunksPerFrame;
                foreach (var player in players)
                {
                    var started = StartChunks(stream, player, budget);
                    _pending.AddRange(started);
                    budget -= started.Count;
                }
            }
        }

        bool IsAlive(LevelStream stream) => EntityManager.Exists(stream.Layout) && EntityManager.HasComponent<LevelLayout>(stream.Layout);

        /// <summary>Solves the level and builds the chunks around the player start; returns the player start on it.</summary>
        float3 Build(Entity layoutEntity)
        {
            var timer = Stopwatch.StartNew();
            var layout = EntityManager.GetComponentData<LevelLayout>(layoutEntity);
            ref var blob = ref layout.Blob.Value;
            int cells = blob.Width * blob.Height;
            bool useSaved = layout.UseSaved && blob.SavedCells.Length == cells;
            // A saved layout keeps the editor's seed, so its bend matches the preview too
            uint seed = useSaved || !layout.RandomSeed ? layout.Seed : LevelSeed.Take();

            // The solver input with this seed's landmarks in it (a saved layout was solved with the same ones)
            var cornerMasks = blob.CornerMasks.ToArray();
            var lockedCorners = blob.LockedCorners.ToArray();
            var initialCells = blob.InitialCells.ToArray();
            var landmarks = WfcLayoutCompiler.PlaceLandmarks(seed, blob.Width, blob.Height, cornerMasks, lockedCorners, initialCells,
                                                             LandmarkPools(ref blob), KeepClear(ref blob), new Vector2Int(blob.PlayerStart.x, blob.PlayerStart.y));

            var packed = new NativeArray<int>(cells, Allocator.Temp);
            if (useSaved)
            {
                for (int i = 0; i < cells; i++) packed[i] = blob.SavedCells[i];
            }
            else if (!Solve(ref blob, layout, seed, packed, cornerMasks, lockedCorners, initialCells))
            {
                for (int i = 0; i < cells; i++) packed[i] = WfcSolver.Free;
            }
            long solveMs = timer.ElapsedMilliseconds;

            var deformation = blob.Deformation;
            deformation.Seed = seed;
            int chunkSize = LevelChunkBuilder.ChunkSize;
            int margin = blob.RingMargin;
            var corners = deformation.BuildCorners();
            var stream = new LevelStream
            {
                Layout = layoutEntity,
                Packed = packed.ToArray(),
                Corners = corners,
                Margin = margin,
                JobCorners = new NativeArray<float3>(margin > 0 ? deformation.BuildCorners(margin) : corners, Allocator.Persistent),
                ChunksX = (blob.Width + 2 * margin + chunkSize - 1) / chunkSize,
                ChunksY = (blob.Height + 2 * margin + chunkSize - 1) / chunkSize,
                LoadDistance = layout.LoadDistance,
                ChunksPerFrame = math.max(1, layout.ChunksPerFrame),
            };
            packed.Dispose();
            var surface = LevelSurface.Build(blob.Width, blob.Height, stream.Packed, blob.TileCorners.ToArray(), blob.TileEdges.ToArray(), blob.TileWalkThrough.ToArray(),
                                             cornerMasks, blob.CornerTerrains.ToArray(), blob.CornerHeights.ToArray(), blob.EdgeWalkables.ToArray());
            stream.JobFlow = new NativeArray<float2>(LevelFlow.Build(surface.Terrain, blob.Width, blob.Height, margin), Allocator.Persistent);
            stream.Ground = new float3[corners.Length];
            for (int i = 0; i < corners.Length; i++) stream.Ground[i] = corners[i] + new float3(0f, surface.Height[i], 0f);
            if (margin > 0) stream.Ring = SolveRing(ref blob, layout, seed, surface);
            var lookCounts = LookCounts(ref layout.Geometry.Value);
            stream.Looks = LevelLooks.Pick(blob.Width, blob.Height, stream.Packed, lookCounts, seed);
            if (stream.Ring != null)
                stream.RingLooks = LevelLooks.Pick(blob.Width + 2 * margin, blob.Height + 2 * margin, stream.Ring, lookCounts, math.hash(new uint2(seed, 0xB6C0u)));
            stream.Pieces = new List<LevelPiecePlacement>();
            for (int i = 0; i < blob.Pieces.Length; i++) stream.Pieces.Add(blob.Pieces[i]);
            foreach (var landmark in landmarks)
                stream.Pieces.Add(new LevelPiecePlacement
                {
                    Piece = landmark.Piece, Rotation = landmark.Rotation,
                    Cell = new int2(landmark.Cell.x, landmark.Cell.y), Size = new int2(landmark.Size.x, landmark.Size.y),
                });
            stream.Built = new bool[stream.ChunksX * stream.ChunksY];
            stream.PiecesByChunk = new List<int>[stream.Built.Length];
            for (int i = 0; i < stream.Pieces.Count; i++)
                (stream.PiecesByChunk[ChunkOf(stream, ref blob, stream.Pieces[i].Cell)] ??= new List<int>()).Add(i);

            var materialBuffer = EntityManager.GetBuffer<LevelMaterial>(layoutEntity);
            stream.Materials = new Material[materialBuffer.Length];
            for (int i = 0; i < stream.Materials.Length; i++) stream.Materials[i] = materialBuffer[i].Value.Value;
            var propBuffer = EntityManager.GetBuffer<LevelPropPrefab>(layoutEntity);
            stream.PropPrefabs = new Entity[propBuffer.Length];
            for (int i = 0; i < stream.PropPrefabs.Length; i++) stream.PropPrefabs[i] = propBuffer[i].Value;
            var placementPrefabBuffer = EntityManager.GetBuffer<LevelPlacementPrefab>(layoutEntity);
            stream.PlacementPrefabs = new Entity[placementPrefabBuffer.Length];
            for (int i = 0; i < stream.PlacementPrefabs.Length; i++) stream.PlacementPrefabs[i] = placementPrefabBuffer[i].Value;
            stream.PlacedPropsByChunk = new List<PlacementPoint>[stream.Built.Length];
            _streams.Add(stream);

            // Spawners in this level: their cells' centers on the bent grid (known without their chunks)
            using var spawners = SystemAPI.QueryBuilder().WithAllRW<LevelEnemySpawner>().Build().ToEntityArray(Allocator.Temp);
            foreach (var entity in spawners)
            {
                var spawner = EntityManager.GetComponentData<LevelEnemySpawner>(entity);
                spawner.Position = GridToWorld(ref blob, stream.Ground, spawner.Cell.x + 0.5f, spawner.Cell.y + 0.5f);
                EntityManager.SetComponentData(entity, spawner);
            }

            // Procedural placement: spawners now, props with their chunks
            var (placedProps, placedSpawners) = Place(stream, ref blob, seed, surface, initialCells);
            if (EntityManager.HasComponent<LevelBackdropData>(layoutEntity)) BuildBackdrop(layoutEntity, ref blob, stream, seed);
            else ClearBackdrop();
            // The forest mask (height fog) and ground height (canopy light), for the eyes only
            if (World.GetExistingSystemManaged<PresentationSystemGroup>() != null) SetForestMask(ref blob, stream, surface);
            // Fireflies, pollen, leaves, mist over the water and splashes (the layout's ambient graph), for the eyes only
            if (EntityManager.HasComponent<LevelAmbientGraph>(layoutEntity) && World.GetExistingSystemManaged<PresentationSystemGroup>() != null)
                LevelAmbientGraphView.Build(EntityManager.GetComponentData<LevelAmbientGraph>(layoutEntity).Graph.Value, ref blob, stream.Ground,
                                            surface.Terrain, stream.JobFlow.ToArray(), stream.Margin, _forestMask, _forestMatrix);
            else if (World.GetExistingSystemManaged<PresentationSystemGroup>() != null) LevelAmbientGraphView.Clear();
            // Moss, leaves, mud and flowers painted on the floors (the layout's decal set), for the eyes only
            if (EntityManager.HasBuffer<LevelDecal>(layoutEntity) && World.GetExistingSystemManaged<PresentationSystemGroup>() != null)
            {
                var decals = EntityManager.GetBuffer<LevelDecal>(layoutEntity).ToNativeArray(Allocator.Temp);
                LevelDecalsView.Build(decals, ref blob, stream.Ground, surface.Terrain, seed);
                decals.Dispose();
            }
            else if (World.GetExistingSystemManaged<PresentationSystemGroup>() != null) LevelDecalsView.Clear();
            // Animals are only for the eyes: worlds that render get them (a dedicated server doesn't)
            if (EntityManager.HasBuffer<LevelAmbientCreature>(layoutEntity) && World.GetExistingSystemManaged<PresentationSystemGroup>() != null)
                BuildAmbientLife(layoutEntity, ref blob, stream, seed, surface);

            // Hand-placed prefabs (portals, exits, chests, boss spawn points; few), all at once so they exist before their
            // chunks stream in: at their cell's center, turned with the bent grid there
            for (int i = 0; i < blob.Prefabs.Length; i++)
            {
                var placed = blob.Prefabs[i];
                if (placed.Prefab < 0 || placed.Prefab >= stream.PlacementPrefabs.Length || stream.PlacementPrefabs[placed.Prefab] == Entity.Null) continue;
                var turn = math.mul(LevelDeformation.CellTurn(stream.Corners, blob.Width, blob.Height, placed.Cell.x, placed.Cell.y),
                                    quaternion.RotateY(math.radians(90f * placed.Rotation)));
                Spawn(stream.PlacementPrefabs[placed.Prefab], GridToWorld(ref blob, stream.Ground, placed.Cell.x + 0.5f, placed.Cell.y + 0.5f),
                      math.mul(blob.Rotation, turn));
            }

            // The start's surroundings, all at once (their jobs run in parallel): the player spawns on them
            var playerStart = GridToWorld(ref blob, stream.Ground, blob.PlayerStart.x + 0.5f, blob.PlayerStart.y + 0.5f);
            long chunksStart = timer.ElapsedMilliseconds;
            var first = StartChunks(stream, playerStart, int.MaxValue);
            foreach (var chunk in first) Finish(chunk);

            Debug.Log($"Level built: {blob.Width}x{blob.Height} cells of {blob.TileSize} m, seed {seed}. Solve {solveMs} ms; " +
                      $"placed {placedProps} props and {placedSpawners} spawners, plus {blob.Prefabs.Length} hand-placed prefabs; " +
                      $"first {first.Count} of {stream.Built.Length} chunks {timer.ElapsedMilliseconds - chunksStart} ms; " +
                      $"the rest stream in within {stream.LoadDistance} m of the player.");
            return playerStart;
        }

        static readonly int AbyssColorId = Shader.PropertyToID("_PL_AbyssColor"), AbyssTopId = Shader.PropertyToID("_PL_AbyssTop"),
                            AbyssDepthId = Shader.PropertyToID("_PL_AbyssDepth"), AbyssBandsId = Shader.PropertyToID("_PL_AbyssBands");

        /// <summary>
        /// The level's backdrop (LevelBackdrop): turns on the abyss fade below the level (global shader values, the same
        /// for every camera), spawns the mist at the level's center and scatters the silhouettes around the background
        /// ring from the seed (so every machine places the same ones).
        /// </summary>
        void BuildBackdrop(Entity layoutEntity, ref LevelLayoutBlob blob, LevelStream stream, uint seed)
        {
            var backdrop = EntityManager.GetComponentData<LevelBackdropData>(layoutEntity);
            Shader.SetGlobalColor(AbyssColorId, new Color(backdrop.AbyssColor.x, backdrop.AbyssColor.y, backdrop.AbyssColor.z, 1f));
            Shader.SetGlobalFloat(AbyssTopId, blob.Origin.y - backdrop.FadeStart);
            Shader.SetGlobalFloat(AbyssDepthId, backdrop.FadeDepth);
            Shader.SetGlobalFloat(AbyssBandsId, backdrop.FadeBands);

            // The level's size in grid space (meters), and the ring's reach past it
            var size = new float2(blob.Width, blob.Height) * blob.TileSize;
            float ring = stream.Margin * blob.TileSize;
            if (backdrop.Mist != Entity.Null)
                Spawn(backdrop.Mist, blob.Origin + math.mul(blob.Rotation, new float3(size.x * 0.5f, 0f, size.y * 0.5f)), blob.Rotation);

            var pieces = EntityManager.GetBuffer<LevelBackdropPiece>(layoutEntity).ToNativeArray(Allocator.Temp);
            if (pieces.Length == 0) return;
            var random = new Unity.Mathematics.Random(math.max(1u, math.hash(new uint2(seed, 0xBAC4u))));
            var placed = new List<float2>();
            float reach = ring + backdrop.PieceDistance.y;
            for (int attempt = 0; attempt < backdrop.PieceCount * 30 && placed.Count < backdrop.PieceCount; attempt++)
            {
                // A point in the band PieceDistance outside the ring, away from the others
                var point = random.NextFloat2(-reach, size + reach);
                float outside = math.length(math.max(float2.zero, math.max(-point, point - size))) - ring;
                if (outside < backdrop.PieceDistance.x || outside > backdrop.PieceDistance.y) continue;
                bool crowded = false;
                foreach (var other in placed) crowded |= math.distance(other, point) < backdrop.PieceSpacing;
                if (crowded) continue;
                placed.Add(point);

                var prefab = pieces[random.NextInt(pieces.Length)].Value;
                float depth = random.NextFloat(backdrop.PieceDepth.x, backdrop.PieceDepth.y);
                Spawn(prefab, blob.Origin + math.mul(blob.Rotation, new float3(point.x, -depth, point.y)),
                      math.mul(blob.Rotation, quaternion.RotateY(random.NextFloat(2f * math.PI))),
                      random.NextFloat(backdrop.PieceScale.x, backdrop.PieceScale.y));
            }
            pieces.Dispose();
        }

        // No level, no abyss fade
        static void ClearBackdrop() => Shader.SetGlobalFloat(AbyssDepthId, 0f);
        static readonly int ForestMaskId = Shader.PropertyToID("_PL_ForestMask"), ForestMaskMatrixId = Shader.PropertyToID("_PL_ForestMaskMatrix"),
                            GroundYId = Shader.PropertyToID("_PL_GroundY");
        Texture2D _forestMask;
        Matrix4x4 _forestMatrix;

        /// <summary>
        /// The level's forest mask (LevelForestMask) as a global texture, with the matrix from world positions to its uv
        /// (the unbent grid: the mask is blurred, so the bend doesn't show), and the level's ground height.
        /// </summary>
        void SetForestMask(ref LevelLayoutBlob blob, LevelStream stream, LevelSurface surface)
        {
            int margin = stream.Margin;
            int w = blob.Width + 1 + 2 * margin, h = blob.Height + 1 + 2 * margin;
            var values = LevelForestMask.Build(surface.Terrain, blob.Width, blob.Height, margin);
            if (_forestMask == null || _forestMask.width != w || _forestMask.height != h)
            {
                if (_forestMask != null) Object.Destroy(_forestMask);
                _forestMask = new Texture2D(w, h, TextureFormat.R8, false, true)
                    { name = "PL_ForestMask", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            }
            var pixels = new byte[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)math.round(math.saturate(values[i]) * 255f);
            _forestMask.SetPixelData(pixels, 0);
            _forestMask.Apply(false);

            // World to grid meters (x east, z north), then to the mask's texel centers (corner 0 of the level is texel margin)
            var toGrid = Matrix4x4.TRS(blob.Origin, blob.Rotation, Vector3.one).inverse;
            var toUv = Matrix4x4.Translate(new Vector3((margin + 0.5f) / w, 0f, (margin + 0.5f) / h)) *
                       Matrix4x4.Scale(new Vector3(1f / (blob.TileSize * w), 1f, 1f / (blob.TileSize * h)));
            Shader.SetGlobalTexture(ForestMaskId, _forestMask);
            _forestMatrix = toUv * toGrid;
            Shader.SetGlobalMatrix(ForestMaskMatrixId, _forestMatrix);
            // The mist wake (skills push and clear the mist) covers the same area
            MistWake.SetArea(_forestMatrix, new Vector2(blob.TileSize * w, blob.TileSize * h));
            Shader.SetGlobalFloat(GroundYId, blob.Origin.y);
        }

        // No level, no forest. Only the world that made them (one that renders: it has a mask) clears the views; a world
        // without one (a server) leaves them alone, and it can't ask for systems while it's being destroyed
        void ClearForestMask()
        {
            if (_forestMask == null) return;
            LevelAmbientGraphView.Clear();
            MistWake.Clear();
            LevelDecalsView.Clear();
            Shader.SetGlobalTexture(ForestMaskId, Texture2D.blackTexture);
            Object.Destroy(_forestMask);
            _forestMask = null;
        }


        /// <summary>
        /// The level's small animals (AmbientLifeSet), placed from the seed: darters on walkable corners beside not-walkable
        /// ground (the forest's edge), flyers on corners with walkable ground all around (also where birds land, the
        /// layout's AmbientPerch buffer), swimmers on water corners with water on at least three sides.
        /// </summary>
        void BuildAmbientLife(Entity layoutEntity, ref LevelLayoutBlob blob, LevelStream stream, uint seed, LevelSurface surface)
        {
            int cw = blob.Width + 1, ch = blob.Height + 1;
            byte TerrainAt(int x, int y) => x < 0 || y < 0 || x >= cw || y >= ch ? (byte)WfcTerrain.NotWalkable : surface.Terrain[x + y * cw];
            var edge = new List<int2>();
            var open = new List<int2>();
            var water = new List<int2>();
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < cw; x++)
                {
                    byte terrain = TerrainAt(x, y);
                    int walls = 0, wet = 0, walkable = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            byte other = TerrainAt(x + dx, y + dy);
                            bool side = dx == 0 || dy == 0;
                            if (side && other == (byte)WfcTerrain.NotWalkable) walls++;
                            if (side && other == (byte)WfcTerrain.Water) wet++;
                            if (other == (byte)WfcTerrain.Walkable) walkable++;
                        }
                    if (terrain == (byte)WfcTerrain.Walkable && walls > 0) edge.Add(new int2(x, y));
                    else if (terrain == (byte)WfcTerrain.Walkable && walkable == 8) open.Add(new int2(x, y));
                    else if (terrain == (byte)WfcTerrain.Water && wet >= 3) water.Add(new int2(x, y));
                }

            var creatures = EntityManager.GetBuffer<LevelAmbientCreature>(layoutEntity).ToNativeArray(Allocator.Temp);
            var perches = EntityManager.AddBuffer<AmbientPerch>(layoutEntity);
            foreach (var corner in open) perches.Add(new AmbientPerch { Position = GridToWorld(ref blob, stream.Ground, corner.x, corner.y) });

            var random = new Unity.Mathematics.Random(math.max(1u, math.hash(new uint2(seed, 0xA11Fu))));
            foreach (var creature in creatures)
            {
                var spots = creature.Kind == AmbientLifeSet.Kind.Darter ? edge : creature.Kind == AmbientLifeSet.Kind.Flyer ? open : water;
                for (int i = 0; i < math.min(creature.Count, spots.Count); i++)
                {
                    // A random spot not used yet (the list's unused part is from i on)
                    int pick = random.NextInt(i, spots.Count);
                    (spots[i], spots[pick]) = (spots[pick], spots[i]);
                    var home = GridToWorld(ref blob, stream.Ground, spots[i].x, spots[i].y) + new float3(0f, creature.Height, 0f);
                    float yaw = random.NextFloat(2f * math.PI);
                    var entity = Spawn(creature.Prefab, home, quaternion.RotateY(yaw), random.NextFloat(creature.Scale.x, creature.Scale.y));
                    EntityManager.AddComponentData(entity, new AmbientCreature
                    {
                        Kind = creature.Kind, Home = home, From = home, To = home, Yaw = yaw, Rest = random.NextFloat(0.5f, 4f),
                        FleeRadius = creature.FleeRadius, Speed = creature.Speed,
                        Random = new Unity.Mathematics.Random(math.max(1u, random.NextUInt())),
                    });
                }
            }
            creatures.Dispose();
        }

        // The blob's landmarks as WfcLayoutCompiler.PlaceLandmarks takes them
        static List<LandmarkPool> LandmarkPools(ref LevelLayoutBlob blob)
        {
            var pools = new List<LandmarkPool>();
            for (int i = 0; i < blob.Landmarks.Length; i++)
            {
                ref var landmark = ref blob.Landmarks[i];
                var pool = new LandmarkPool { MinCount = landmark.MinCount, MaxCount = landmark.MaxCount, Spacing = landmark.Spacing };
                for (int o = 0; o < landmark.Options.Length; o++)
                {
                    ref var option = ref landmark.Options[o];
                    var corners = new int[option.Corners.Length];
                    for (int c = 0; c < corners.Length; c++) corners[c] = option.Corners[c];
                    pool.Options.Add(new LandmarkOption { Piece = option.Piece, Rotation = option.Rotation, Width = option.Width, Height = option.Height, Corners = corners });
                }
                pools.Add(pool);
            }
            return pools;
        }

        static List<Vector2Int> KeepClear(ref LevelLayoutBlob blob)
        {
            var cells = new List<Vector2Int>();
            for (int i = 0; i < blob.KeepClear.Length; i++) cells.Add(new Vector2Int(blob.KeepClear[i].x, blob.KeepClear[i].y));
            return cells;
        }

        // How many looks each tile has (LevelGeometryBlob.TileLookStart)
        static int[] LookCounts(ref LevelGeometryBlob geometry)
        {
            var counts = new int[math.max(0, geometry.TileLookStart.Length - 1)];
            for (int t = 0; t < counts.Length; t++) counts[t] = geometry.TileLookStart[t + 1] - geometry.TileLookStart[t];
            return counts;
        }

        // The chunk a level cell is built in (chunks cover the level and its background ring)
        static int ChunkOf(LevelStream stream, ref LevelLayoutBlob blob, int2 cell)
        {
            var chunk = (math.clamp(cell, 0, new int2(blob.Width - 1, blob.Height - 1)) + stream.Margin) / LevelChunkBuilder.ChunkSize;
            return chunk.x + chunk.y * stream.ChunksX;
        }

        /// <summary>Starts up to budget unbuilt chunks within LoadDistance of focus, nearest first.</summary>
        List<PendingChunk> StartChunks(LevelStream stream, float3 focus, int budget)
        {
            var started = new List<PendingChunk>();
            var layout = EntityManager.GetComponentData<LevelLayout>(stream.Layout);
            ref var blob = ref layout.Blob.Value;
            int chunkSize = LevelChunkBuilder.ChunkSize;
            // A chunk counts as near when any part of it could be: its center's distance minus half its diagonal
            float halfDiagonal = chunkSize * blob.TileSize * 0.7072f;

            var candidates = new List<(float Distance, int Chunk)>();
            for (int cy = 0; cy < stream.ChunksY; cy++)
                for (int cx = 0; cx < stream.ChunksX; cx++)
                {
                    int chunk = cx + cy * stream.ChunksX;
                    if (stream.Built[chunk]) continue;
                    // Middle of the chunk (edge chunks can be cut short by the grid), in level cells
                    float midX = (cx * chunkSize + math.min((cx + 1) * chunkSize, blob.Width + 2 * stream.Margin)) * 0.5f - stream.Margin;
                    float midY = (cy * chunkSize + math.min((cy + 1) * chunkSize, blob.Height + 2 * stream.Margin)) * 0.5f - stream.Margin;
                    var center = GridToWorld(ref blob, stream.Corners, midX, midY);
                    float distance = math.distance(center.xz, focus.xz) - halfDiagonal;
                    if (distance <= stream.LoadDistance) candidates.Add((distance, chunk));
                }
            if (candidates.Count == 0) return started;
            candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            for (int i = 0; i < math.min(budget, candidates.Count); i++)
                started.Add(StartChunk(stream, ref blob, layout.Geometry, candidates[i].Chunk));
            return started;
        }

        // Lists the chunk's tiles and pieces and starts its jobs
        PendingChunk StartChunk(LevelStream stream, ref LevelLayoutBlob blob, BlobAssetReference<LevelGeometryBlob> geometryBlob, int chunk)
        {
            stream.Built[chunk] = true;
            ref var geometry = ref geometryBlob.Value;
            int chunkSize = LevelChunkBuilder.ChunkSize;
            int cx = chunk % stream.ChunksX, cy = chunk / stream.ChunksX;
            var instances = new List<LevelChunkBuilder.Instance>();
            // Chunk cells are on the grid with the background ring: the level's cells start at (Margin, Margin)
            int margin = stream.Margin, width = blob.Width + 2 * margin, height = blob.Height + 2 * margin;

            for (int y = cy * chunkSize; y < math.min(height, (cy + 1) * chunkSize); y++)
                for (int x = cx * chunkSize; x < math.min(width, (cx + 1) * chunkSize); x++)
                {
                    int lx = x - margin, ly = y - margin;
                    bool inLevel = lx >= 0 && ly >= 0 && lx < blob.Width && ly < blob.Height;
                    int cell = inLevel ? stream.Packed[lx + ly * blob.Width] : stream.Ring != null ? stream.Ring[x + y * width] : -1;
                    if (cell < 0) continue;
                    int tile = LevelLayoutAuthoring.PackedTile(cell);
                    if (tile + 1 >= geometry.TileLookStart.Length) continue;
                    int look = inLevel ? stream.Looks[lx + ly * blob.Width] : stream.RingLooks?[x + y * width] ?? 0;
                    instances.Add(new LevelChunkBuilder.Instance
                    {
                        Part = geometry.TileLookStart[tile] + look, Center = new float2(x + 0.5f, y + 0.5f),
                        Rotation = LevelLayoutAuthoring.PackedRotation(cell), Mirror = LevelLayoutAuthoring.PackedMirror(cell),
                        Raise = tile < blob.TileRaises.Length ? blob.TileRaises[tile] : 0f,
                        NoColliders = !inLevel,
                    });
                }

            if (stream.PiecesByChunk[chunk] != null)
                foreach (int i in stream.PiecesByChunk[chunk])
                {
                    var piece = stream.Pieces[i];
                    if (piece.Piece >= geometry.Pieces.Length) continue;
                    instances.Add(new LevelChunkBuilder.Instance
                    {
                        IsPiece = true, Part = piece.Piece, Rotation = piece.Rotation,
                        Center = new float2(piece.Cell + margin) + new float2(piece.Size) * 0.5f,
                        Raise = piece.Piece < blob.PieceRaises.Length ? blob.PieceRaises[piece.Piece] : 0f,
                    });
                }

            var builder = LevelChunkBuilder.Schedule(geometryBlob, stream.JobCorners, stream.JobFlow, width, height, blob.TileSize, instances);
            return new PendingChunk { Stream = stream, Builder = builder, Chunk = chunk };
        }

        // Makes the chunk's meshes, colliders and props (waits for its jobs if they're still running)
        void Finish(PendingChunk pending)
        {
            var stream = pending.Stream;
            var layout = EntityManager.GetComponentData<LevelLayout>(stream.Layout);
            ref var blob = ref layout.Blob.Value;
            pending.Builder.Finish(EntityManager, blob.Origin, blob.Rotation, stream.Materials, _meshes, _colliders);
            // Procedural props placed in this chunk: on the bent floor, turned by their yaw
            int chunkIndex = pending.Chunk;
            if (stream.PlacedPropsByChunk[chunkIndex] != null)
                foreach (var placed in stream.PlacedPropsByChunk[chunkIndex])
                {
                    int prefab = blob.PlacementRules[placed.Rule].Prefab;
                    if (prefab < 0 || prefab >= stream.PlacementPrefabs.Length || stream.PlacementPrefabs[prefab] == Entity.Null) continue;
                    Spawn(stream.PlacementPrefabs[prefab], GridToWorld(ref blob, stream.Corners, placed.Grid.x, placed.Grid.y) + new float3(0f, placed.Height, 0f),
                          math.mul(blob.Rotation, quaternion.RotateY(placed.Yaw)));
                }
            foreach (var prop in pending.Builder.Props)
            {
                if (prop.Prefab < 0 || prop.Prefab >= stream.PropPrefabs.Length || stream.PropPrefabs[prop.Prefab] == Entity.Null) continue;
                Spawn(stream.PropPrefabs[prop.Prefab], blob.Origin + math.mul(blob.Rotation, prop.Position), math.mul(blob.Rotation, prop.Rotation));
            }
        }

        bool Solve(ref LevelLayoutBlob blob, LevelLayout layout, uint seed, NativeArray<int> packed, ulong[] cornerMasks, byte[] lockedCorners, int[] initialCells)
        {
            int cells = blob.Width * blob.Height;
            int variants = blob.VariantWeights.Length;

            var job = new WfcSolveJob
            {
                Width = blob.Width,
                Height = blob.Height,
                VariantSlots = Copy(ref blob.VariantSlots, Allocator.TempJob),
                VariantWeights = Copy(ref blob.VariantWeights, Allocator.TempJob),
                VariantWalkThrough = Copy(ref blob.VariantWalkThrough, Allocator.TempJob),
                InitialCornerMasks = new NativeArray<ulong>(cornerMasks, Allocator.TempJob),
                InitialEdgeMasks = Copy(ref blob.EdgeMasks, Allocator.TempJob),
                InitialCells = new NativeArray<int>(initialCells, Allocator.TempJob),
                Seed = seed,
                MaxAttempts = layout.MaxAttempts,
                Result = new NativeArray<int>(cells, Allocator.TempJob),
                Attempts = new NativeReference<int>(Allocator.TempJob),
                Connect = blob.Connect,
                StartCorner = blob.StartCorner,
                FillPocketsSmallerThan = blob.FillPocketsSmallerThan,
                WalkableCorners = blob.WalkableCorners,
                NotWalkableCorners = blob.NotWalkableCorners,
                WalkableEdges = blob.WalkableEdges,
                CornerHeights = Copy(ref blob.CornerHeights, Allocator.TempJob),
                LockedCorners = new NativeArray<byte>(lockedCorners, Allocator.TempJob),
                Repairs = new NativeArray<byte>(blob.CornerMasks.Length, Allocator.TempJob),
                UnconnectedPockets = new NativeReference<int>(Allocator.TempJob),
                Backtracks = new NativeReference<int>(Allocator.TempJob),
            };
            job.Schedule().Complete();

            int attempts = job.Attempts.Value;
            if (attempts > 0)
            {
                for (int i = 0; i < cells; i++)
                {
                    int v = job.Result[i];
                    packed[i] = v >= 0 && v < variants ? LevelLayoutAuthoring.Pack(blob.VariantTile[v], blob.VariantRotation[v], blob.VariantMirror[v] != 0) : v;
                }
                int carved = 0, filled = 0;
                foreach (var repair in job.Repairs)
                {
                    if (repair == WfcSolver.RepairCarved) carved++;
                    else if (repair == WfcSolver.RepairFilled) filled++;
                }
                string connected = !blob.Connect ? "" : $" Connected: {carved} corners carved, {filled} filled" +
                                   (job.UnconnectedPockets.Value > 0 ? $", {job.UnconnectedPockets.Value} pockets left (locked corners in the way)." : ".");
                // Every machine builds the level from the seed: the same hash on host and clients means the same level
                uint hash = 2166136261u;
                for (int i = 0; i < cells; i++) hash = (hash ^ (uint)packed[i]) * 16777619u;
                Debug.Log($"Level generated with seed {seed}" + (attempts > 1 ? $" (attempt {attempts})" : "") + $" and {job.Backtracks.Value} backtracks, cells hash {hash:X8}." + connected);
            }
            else
            {
                Debug.LogError($"Level generation failed with seed {seed} after {layout.MaxAttempts} attempts. The corner rules may " +
                               "conflict, or the tile set lacks tiles for some corner combinations (see the tile set's coverage).");
            }

            job.DisposeArrays();
            return attempts > 0;
        }

        /// <summary>
        /// Solves the background ring around the solved level (its border fixed to the level's, so they meet exactly) and
        /// returns its cells on the grid with the ring (Tile * 4 + Rotation; the level's cells are Occupied), or null when
        /// no ring fits.
        /// </summary>
        int[] SolveRing(ref LevelLayoutBlob blob, LevelLayout layout, uint seed, LevelSurface surface)
        {
            ref var ring = ref blob.Ring;
            var cornerMasks = ring.CornerMasks.ToArray();
            var edgeMasks = ring.EdgeMasks.ToArray();
            WfcLayoutCompiler.FixRingBorder(blob.Width, blob.Height, blob.RingMargin, cornerMasks, edgeMasks, surface);
            int cells = ring.Width * ring.Height;
            var job = new WfcSolveJob
            {
                Width = ring.Width,
                Height = ring.Height,
                VariantSlots = Copy(ref ring.VariantSlots, Allocator.TempJob),
                VariantWeights = Copy(ref ring.VariantWeights, Allocator.TempJob),
                VariantWalkThrough = Copy(ref ring.VariantWalkThrough, Allocator.TempJob),
                InitialCornerMasks = new NativeArray<ulong>(cornerMasks, Allocator.TempJob),
                InitialEdgeMasks = new NativeArray<ulong>(edgeMasks, Allocator.TempJob),
                InitialCells = Copy(ref ring.InitialCells, Allocator.TempJob),
                Seed = math.hash(new uint2(seed, 0xB6C0u)),
                MaxAttempts = layout.MaxAttempts,
                StartCorner = -1,
                CornerHeights = Copy(ref blob.CornerHeights, Allocator.TempJob),
                LockedCorners = new NativeArray<byte>(cornerMasks.Length, Allocator.TempJob),
                Result = new NativeArray<int>(cells, Allocator.TempJob),
                Attempts = new NativeReference<int>(Allocator.TempJob),
                Repairs = new NativeArray<byte>(cornerMasks.Length, Allocator.TempJob),
                UnconnectedPockets = new NativeReference<int>(Allocator.TempJob),
                Backtracks = new NativeReference<int>(Allocator.TempJob),
            };
            job.Schedule().Complete();

            int[] packed = null;
            if (job.Attempts.Value > 0)
            {
                packed = new int[cells];
                for (int i = 0; i < cells; i++)
                {
                    int v = job.Result[i];
                    packed[i] = v >= 0 ? LevelLayoutAuthoring.Pack(ring.VariantTile[v], ring.VariantRotation[v], ring.VariantMirror[v] != 0) : v;
                }
            }
            else Debug.LogWarning($"No background ring fits around the level with seed {seed}: the Background tiles lack combinations its edge needs.");
            job.DisposeArrays();
            return packed;
        }

        /// <summary>
        /// Runs the placement rules (LevelPlacementJob) on the finished layout: spawners become LevelEnemySpawner entities
        /// with a group picked by weight, props are kept per chunk until it's built. Returns how many of each.
        /// </summary>
        (int Props, int Spawners) Place(LevelStream stream, ref LevelLayoutBlob blob, uint seed, LevelSurface surface, int[] initialCells)
        {
            if (blob.PlacementRules.Length == 0) return (0, 0);
            int cells = blob.Width * blob.Height;
            var occupied = new NativeArray<bool>(cells, Allocator.TempJob);
            for (int i = 0; i < cells; i++) occupied[i] = initialCells[i] == WfcSolver.Occupied;
            var rules = new NativeArray<PlacementRuleData>(blob.PlacementRules.Length, Allocator.TempJob);
            for (int r = 0; r < rules.Length; r++) rules[r] = blob.PlacementRules[r].Data;
            var reserved = new NativeArray<float3>(blob.Prefabs.Length, Allocator.TempJob);
            for (int i = 0; i < reserved.Length; i++) reserved[i] = new float3(new float2(blob.Prefabs[i].Cell) + 0.5f, blob.Prefabs[i].ReservedRadius);

            var job = new LevelPlacementJob
            {
                Width = blob.Width, Height = blob.Height, TileSize = blob.TileSize, Seed = seed,
                StartCorner = blob.StartCorner, Start = new float2(blob.PlayerStart) + 0.5f,
                CornerTerrain = new NativeArray<byte>(surface.Terrain, Allocator.TempJob),
                EdgeConnection = new NativeArray<int>(surface.EdgeConnection, Allocator.TempJob),
                CornerHeight = new NativeArray<float>(surface.Height, Allocator.TempJob),
                EdgeWalkable = new NativeArray<byte>(surface.EdgeWalkable, Allocator.TempJob),
                OccupiedCells = occupied, BentCorners = stream.JobCorners, Rules = rules, Reserved = reserved,
                Points = new NativeList<PlacementPoint>(Allocator.TempJob),
            };
            job.Schedule().Complete();

            // Copied out first: creating spawner entities is a structural change that invalidates the buffer
            var enemies = EntityManager.GetBuffer<LevelPlacementEnemy>(stream.Layout).ToNativeArray(Allocator.Temp);
            int props = 0, spawners = 0;
            for (int i = 0; i < job.Points.Length; i++)
            {
                var point = job.Points[i];
                ref var rule = ref blob.PlacementRules[point.Rule];
                if (rule.Data.Kind == PlacementKind.Prop)
                {
                    int chunk = ChunkOf(stream, ref blob, (int2)math.floor(point.Grid));
                    (stream.PlacedPropsByChunk[chunk] ??= new List<PlacementPoint>()).Add(point);
                    props++;
                    continue;
                }

                // A spawner: pick a group by weight (deterministic per seed and point)
                float total = 0f;
                for (int g = 0; g < rule.GroupCount; g++) total += math.max(0f, blob.PlacementGroups[rule.GroupStart + g].Weight);
                if (total <= 0f) continue;
                var random = new Unity.Mathematics.Random(math.max(1u, math.hash(new uint2(seed, (uint)i + 7919u))));
                float pick = random.NextFloat(total);
                int chosen = rule.GroupStart;
                for (int g = 0; g < rule.GroupCount; g++)
                {
                    pick -= math.max(0f, blob.PlacementGroups[rule.GroupStart + g].Weight);
                    if (pick <= 0f) { chosen = rule.GroupStart + g; break; }
                }
                var group = blob.PlacementGroups[chosen];
                if (group.EnemyCount == 0) continue;

                var spawner = EntityManager.CreateEntity();
                EntityManager.AddComponentData(spawner, new LevelEnemySpawner
                {
                    Cell = (int2)math.floor(point.Grid),
                    Position = GridToWorld(ref blob, stream.Corners, point.Grid.x, point.Grid.y) + new float3(0f, point.Height, 0f),
                    TriggerRadius = rule.TriggerRadius,
                    SpawnRadius = rule.SpawnRadius,
                });
                var entries = EntityManager.AddBuffer<LevelEnemySpawnerEntry>(spawner);
                for (int e = group.EnemyStart; e < group.EnemyStart + group.EnemyCount; e++)
                    entries.Add(new LevelEnemySpawnerEntry { Definition = enemies[e].Definition, Count = enemies[e].Count });
                spawners++;
            }

            enemies.Dispose();
            job.CornerTerrain.Dispose();
            job.EdgeConnection.Dispose();
            job.CornerHeight.Dispose();
            job.EdgeWalkable.Dispose();
            job.OccupiedCells.Dispose();
            job.Rules.Dispose();
            job.Reserved.Dispose();
            job.Points.Dispose();
            return (props, spawners);
        }

        static NativeArray<T> Copy<T>(ref BlobArray<T> source, Allocator allocator) where T : unmanaged
        {
            var array = new NativeArray<T>(source.Length, allocator);
            for (int i = 0; i < source.Length; i++) array[i] = source[i];
            return array;
        }

        /// <summary>World position of a grid point (in cells) on the bent grid, turned around the origin with the layout.</summary>
        static float3 GridToWorld(ref LevelLayoutBlob blob, float3[] corners, float x, float y) =>
            blob.Origin + math.mul(blob.Rotation, LevelDeformation.Sample(corners, blob.Width, blob.Height, x, y));

        // Props are prefabs: placed whole. Unity Physics bakes static colliders unparented, in the prefab's space, so
        // those are moved the same way; parented parts follow the root. Returns the root.
        Entity Spawn(Entity prefab, float3 position, quaternion turn, float scale = 0f)
        {
            var root = EntityManager.Instantiate(prefab);
            if (EntityManager.HasComponent<LocalTransform>(root))
            {
                var transform = EntityManager.GetComponentData<LocalTransform>(root);
                transform.Position = position;
                transform.Rotation = turn;
                if (scale > 0f) transform.Scale = scale;   // 0 keeps the prefab's
                EntityManager.SetComponentData(root, transform);
            }

            if (!EntityManager.HasBuffer<LinkedEntityGroup>(root)) return root;
            var linked = EntityManager.GetBuffer<LinkedEntityGroup>(root).ToNativeArray(Allocator.Temp);
            foreach (var member in linked)
            {
                var entity = member.Value;
                if (entity == root || EntityManager.HasComponent<Parent>(entity) || !EntityManager.HasComponent<LocalTransform>(entity)) continue;
                var transform = EntityManager.GetComponentData<LocalTransform>(entity);
                transform.Position = position + math.mul(turn, transform.Position);
                transform.Rotation = math.mul(turn, transform.Rotation);
                EntityManager.SetComponentData(entity, transform);
            }
            linked.Dispose();
            return root;
        }
    }

    /// <summary>
    /// Spawns each level enemy group once, when any player first comes within its TriggerRadius (horizontally), through
    /// EnemyManager, which puts the enemies on the ground. Waits for the level and the enemy prefabs. Only the host spawns.
    /// </summary>
    [WorldSystemFilter(GameWorlds.Server)]
    public partial class LevelEnemySpawnerSystem : SystemBase
    {
        EntityQuery _players;

        protected override void OnCreate()
        {
            _players = SystemAPI.QueryBuilder().WithAll<NetPlayer, LocalTransform>().Build();
            RequireForUpdate<LevelReady>();
            RequireForUpdate<LevelEnemySpawner>();
            RequireForUpdate(_players);
        }

        protected override void OnUpdate()
        {
            var enemyManager = EnemyManager.Instance;
            if (enemyManager == null || !enemyManager.IsReady) return;
            using var players = _players.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            using var spawners = SystemAPI.QueryBuilder().WithAll<LevelEnemySpawner, LevelEnemySpawnerEntry>().Build().ToEntityArray(Allocator.Temp);
            foreach (var entity in spawners)
            {
                var spawner = EntityManager.GetComponentData<LevelEnemySpawner>(entity);
                if (spawner.Triggered || !AnyWithin(players, spawner.Position, spawner.TriggerRadius)) continue;

                spawner.Triggered = true;
                EntityManager.SetComponentData(entity, spawner);
                // Copied first: spawning enemies is a structural change that invalidates the buffer
                var entries = EntityManager.GetBuffer<LevelEnemySpawnerEntry>(entity).ToNativeArray(Allocator.Temp);
                foreach (var entry in entries)
                {
                    var definition = entry.Definition.Value;
                    // More of them at a dungeon's higher difficulties
                    if (definition != null) enemyManager.SpawnGroup(definition, spawner.Position, DungeonDifficulty.EnemyCount(entry.Count), spawner.SpawnRadius, riseIn: true);
                }
                entries.Dispose();
            }
        }

        static bool AnyWithin(NativeArray<LocalTransform> players, float3 position, float radius)
        {
            foreach (var player in players)
                if (math.lengthsq((player.Position - position).xz) <= radius * radius) return true;
            return false;
        }
    }
}
