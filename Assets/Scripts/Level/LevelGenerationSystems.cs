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
    /// the chunks within LoadDistance of the player start before the level is ready, the rest as the player comes within
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
            public float3[] Corners;        // Bent grid
            public NativeArray<float3> JobCorners;  // The same, for the chunk jobs
            public Material[] Materials;
            public Entity[] PropPrefabs;
            public int ChunksX, ChunksY;
            public bool[] Built;
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

        protected override void OnDestroy()
        {
            foreach (var pending in _pending) pending.Builder.Dispose();
            foreach (var stream in _streams) if (stream.JobCorners.IsCreated) stream.JobCorners.Dispose();
            foreach (var collider in _colliders) if (collider.IsCreated) collider.Dispose();
            foreach (var mesh in _meshes) if (mesh != null) Object.Destroy(mesh);
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

            // Stream chunks around the player (before it spawns, the start's chunks are already built)
            if (_streams.Count == 0) return;
            float3? player = null;
            foreach (var transform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<Player>())
                player = transform.ValueRO.Position;
            if (player == null) return;

            for (int i = _streams.Count - 1; i >= 0; i--)
            {
                var stream = _streams[i];
                if (!IsAlive(stream))
                {
                    if (stream.JobCorners.IsCreated && !_pending.Exists(p => p.Stream == stream)) stream.JobCorners.Dispose();
                    _streams.RemoveAt(i);
                    continue;
                }
                _pending.AddRange(StartChunks(stream, player.Value, stream.ChunksPerFrame));
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
            uint seed = useSaved || !layout.RandomSeed ? layout.Seed : (uint)System.DateTime.Now.Ticks;

            var packed = new NativeArray<int>(cells, Allocator.Temp);
            if (useSaved)
            {
                for (int i = 0; i < cells; i++) packed[i] = blob.SavedCells[i];
            }
            else if (!Solve(ref blob, layout, seed, packed))
            {
                for (int i = 0; i < cells; i++) packed[i] = WfcSolver.Free;
            }
            long solveMs = timer.ElapsedMilliseconds;

            var deformation = blob.Deformation;
            deformation.Seed = seed;
            int chunkSize = LevelChunkBuilder.ChunkSize;
            var corners = deformation.BuildCorners();
            var stream = new LevelStream
            {
                Layout = layoutEntity,
                Packed = packed.ToArray(),
                Corners = corners,
                JobCorners = new NativeArray<float3>(corners, Allocator.Persistent),
                ChunksX = (blob.Width + chunkSize - 1) / chunkSize,
                ChunksY = (blob.Height + chunkSize - 1) / chunkSize,
                LoadDistance = layout.LoadDistance,
                ChunksPerFrame = math.max(1, layout.ChunksPerFrame),
            };
            packed.Dispose();
            stream.Built = new bool[stream.ChunksX * stream.ChunksY];
            stream.PiecesByChunk = new List<int>[stream.Built.Length];
            for (int i = 0; i < blob.Pieces.Length; i++)
            {
                var cell = math.clamp(blob.Pieces[i].Cell, 0, new int2(blob.Width - 1, blob.Height - 1)) / chunkSize;
                int chunk = cell.x + cell.y * stream.ChunksX;
                (stream.PiecesByChunk[chunk] ??= new List<int>()).Add(i);
            }

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
                spawner.Position = GridToWorld(ref blob, stream.Corners, spawner.Cell.x + 0.5f, spawner.Cell.y + 0.5f);
                EntityManager.SetComponentData(entity, spawner);
            }

            // Procedural placement: spawners now, props with their chunks
            var (placedProps, placedSpawners) = Place(stream, ref blob, seed);

            // The start's surroundings, all at once (their jobs run in parallel): the player spawns on them
            var playerStart = GridToWorld(ref blob, stream.Corners, blob.PlayerStart.x + 0.5f, blob.PlayerStart.y + 0.5f);
            long chunksStart = timer.ElapsedMilliseconds;
            var first = StartChunks(stream, playerStart, int.MaxValue);
            foreach (var chunk in first) Finish(chunk);

            Debug.Log($"Level built: {blob.Width}x{blob.Height} cells of {blob.TileSize} m, seed {seed}. Solve {solveMs} ms; " +
                      $"placed {placedProps} props and {placedSpawners} spawners; " +
                      $"first {first.Count} of {stream.Built.Length} chunks {timer.ElapsedMilliseconds - chunksStart} ms; " +
                      $"the rest stream in within {stream.LoadDistance} m of the player.");
            return playerStart;
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
                    // Middle of the chunk (edge chunks can be cut short by the grid)
                    float midX = (cx * chunkSize + math.min((cx + 1) * chunkSize, blob.Width)) * 0.5f;
                    float midY = (cy * chunkSize + math.min((cy + 1) * chunkSize, blob.Height)) * 0.5f;
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

            for (int y = cy * chunkSize; y < math.min(blob.Height, (cy + 1) * chunkSize); y++)
                for (int x = cx * chunkSize; x < math.min(blob.Width, (cx + 1) * chunkSize); x++)
                {
                    int cell = stream.Packed[x + y * blob.Width];
                    if (cell < 0) continue;
                    int tile = LevelLayoutAuthoring.PackedTile(cell);
                    if (tile >= geometry.Tiles.Length) continue;
                    instances.Add(new LevelChunkBuilder.Instance
                    {
                        Part = tile, Center = new float2(x + 0.5f, y + 0.5f), Rotation = LevelLayoutAuthoring.PackedRotation(cell),
                    });
                }

            if (stream.PiecesByChunk[chunk] != null)
                foreach (int i in stream.PiecesByChunk[chunk])
                {
                    var piece = blob.Pieces[i];
                    if (piece.Piece >= geometry.Pieces.Length) continue;
                    instances.Add(new LevelChunkBuilder.Instance
                    {
                        IsPiece = true, Part = piece.Piece, Center = new float2(piece.Cell) + new float2(piece.Size) * 0.5f, Rotation = piece.Rotation,
                    });
                }

            var builder = LevelChunkBuilder.Schedule(geometryBlob, stream.JobCorners, blob.Width, blob.Height, blob.TileSize, instances);
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
                    Spawn(stream.PlacementPrefabs[prefab], GridToWorld(ref blob, stream.Corners, placed.Grid.x, placed.Grid.y),
                          math.mul(blob.Rotation, quaternion.RotateY(placed.Yaw)));
                }
            foreach (var prop in pending.Builder.Props)
            {
                if (prop.Prefab < 0 || prop.Prefab >= stream.PropPrefabs.Length || stream.PropPrefabs[prop.Prefab] == Entity.Null) continue;
                Spawn(stream.PropPrefabs[prop.Prefab], blob.Origin + math.mul(blob.Rotation, prop.Position), math.mul(blob.Rotation, prop.Rotation));
            }
        }

        bool Solve(ref LevelLayoutBlob blob, LevelLayout layout, uint seed, NativeArray<int> packed)
        {
            int cells = blob.Width * blob.Height;
            int variants = blob.VariantWeights.Length;

            var job = new WfcSolveJob
            {
                Width = blob.Width,
                Height = blob.Height,
                VariantCorners = Copy(ref blob.VariantCorners, Allocator.TempJob),
                VariantWeights = Copy(ref blob.VariantWeights, Allocator.TempJob),
                InitialCornerMasks = Copy(ref blob.CornerMasks, Allocator.TempJob),
                InitialCells = Copy(ref blob.InitialCells, Allocator.TempJob),
                Seed = seed,
                MaxAttempts = layout.MaxAttempts,
                Result = new NativeArray<int>(cells, Allocator.TempJob),
                Attempts = new NativeReference<int>(Allocator.TempJob),
                Connect = blob.Connect,
                StartCorner = blob.StartCorner,
                FillPocketsSmallerThan = blob.FillPocketsSmallerThan,
                LockedCorners = Copy(ref blob.LockedCorners, Allocator.TempJob),
                Repairs = new NativeArray<byte>(blob.CornerMasks.Length, Allocator.TempJob),
                UnconnectedPockets = new NativeReference<int>(Allocator.TempJob),
            };
            job.Schedule().Complete();

            int attempts = job.Attempts.Value;
            if (attempts > 0)
            {
                for (int i = 0; i < cells; i++)
                {
                    int v = job.Result[i];
                    packed[i] = v >= 0 && v < variants ? LevelLayoutAuthoring.Pack(blob.VariantTile[v], blob.VariantRotation[v]) : v;
                }
                int carved = 0, filled = 0;
                foreach (var repair in job.Repairs)
                {
                    if (repair == WfcSolver.RepairCarved) carved++;
                    else if (repair == WfcSolver.RepairFilled) filled++;
                }
                string connected = !blob.Connect ? "" : $" Connected: {carved} corners carved, {filled} filled" +
                                   (job.UnconnectedPockets.Value > 0 ? $", {job.UnconnectedPockets.Value} pockets left (locked corners in the way)." : ".");
                Debug.Log($"Level generated with seed {seed}" + (attempts > 1 ? $" (attempt {attempts})." : ".") + connected);
            }
            else
            {
                Debug.LogError($"Level generation failed with seed {seed} after {layout.MaxAttempts} attempts. The corner rules may " +
                               "conflict, or the tile set lacks tiles for some corner combinations (see the tile set's coverage).");
            }

            job.VariantCorners.Dispose();
            job.VariantWeights.Dispose();
            job.InitialCornerMasks.Dispose();
            job.InitialCells.Dispose();
            job.Result.Dispose();
            job.LockedCorners.Dispose();
            job.Repairs.Dispose();
            job.UnconnectedPockets.Dispose();
            job.Attempts.Dispose();
            return attempts > 0;
        }

        /// <summary>
        /// Runs the placement rules (LevelPlacementJob) on the finished layout: spawners become LevelEnemySpawner entities
        /// with a group picked by weight, props are kept per chunk until it's built. Returns how many of each.
        /// </summary>
        (int Props, int Spawners) Place(LevelStream stream, ref LevelLayoutBlob blob, uint seed)
        {
            if (blob.PlacementRules.Length == 0) return (0, 0);
            int cells = blob.Width * blob.Height;
            var terrain = LevelPlacement.CornerTerrain(blob.Width, blob.Height, stream.Packed, blob.TileCorners.ToArray(), blob.CornerMasks.ToArray());
            var occupied = new NativeArray<bool>(cells, Allocator.TempJob);
            for (int i = 0; i < cells && i < blob.InitialCells.Length; i++) occupied[i] = blob.InitialCells[i] == WfcSolver.Occupied;
            var rules = new NativeArray<PlacementRuleData>(blob.PlacementRules.Length, Allocator.TempJob);
            for (int r = 0; r < rules.Length; r++) rules[r] = blob.PlacementRules[r].Data;

            var job = new LevelPlacementJob
            {
                Width = blob.Width, Height = blob.Height, TileSize = blob.TileSize, Seed = seed,
                StartCorner = blob.StartCorner, Start = new float2(blob.PlayerStart) + 0.5f,
                CornerTerrain = new NativeArray<byte>(terrain, Allocator.TempJob),
                OccupiedCells = occupied, BentCorners = stream.JobCorners, Rules = rules,
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
                    var chunkCell = math.clamp((int2)math.floor(point.Grid), 0, new int2(blob.Width - 1, blob.Height - 1)) / LevelChunkBuilder.ChunkSize;
                    int chunk = chunkCell.x + chunkCell.y * stream.ChunksX;
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
                    Position = GridToWorld(ref blob, stream.Corners, point.Grid.x, point.Grid.y),
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
            job.OccupiedCells.Dispose();
            job.Rules.Dispose();
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
        // those are moved the same way; parented parts follow the root.
        void Spawn(Entity prefab, float3 position, quaternion turn)
        {
            var root = EntityManager.Instantiate(prefab);
            if (EntityManager.HasComponent<LocalTransform>(root))
            {
                var transform = EntityManager.GetComponentData<LocalTransform>(root);
                transform.Position = position;
                transform.Rotation = turn;
                EntityManager.SetComponentData(root, transform);
            }

            if (!EntityManager.HasBuffer<LinkedEntityGroup>(root)) return;
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
        }
    }

    /// <summary>
    /// Spawns each level enemy group once, when the player first comes within its TriggerRadius (horizontally), through
    /// EnemyManager, which puts the enemies on the ground. Waits for the level and the enemy prefabs.
    /// </summary>
    public partial class LevelEnemySpawnerSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<LevelReady>();
            RequireForUpdate<LevelEnemySpawner>();
        }

        protected override void OnUpdate()
        {
            var enemyManager = EnemyManager.Instance;
            var characterManager = CharacterManager.Instance;
            if (enemyManager == null || !enemyManager.IsReady || characterManager == null) return;
            var player = characterManager.Player;
            if (player == Entity.Null || !EntityManager.Exists(player) || !EntityManager.HasComponent<LocalTransform>(player)) return;
            var playerPosition = EntityManager.GetComponentData<LocalTransform>(player).Position;

            using var spawners = SystemAPI.QueryBuilder().WithAll<LevelEnemySpawner, LevelEnemySpawnerEntry>().Build().ToEntityArray(Allocator.Temp);
            foreach (var entity in spawners)
            {
                var spawner = EntityManager.GetComponentData<LevelEnemySpawner>(entity);
                if (spawner.Triggered) continue;
                var offset = (playerPosition - spawner.Position).xz;
                if (math.lengthsq(offset) > spawner.TriggerRadius * spawner.TriggerRadius) continue;

                spawner.Triggered = true;
                EntityManager.SetComponentData(entity, spawner);
                // Copied first: spawning enemies is a structural change that invalidates the buffer
                var entries = EntityManager.GetBuffer<LevelEnemySpawnerEntry>(entity).ToNativeArray(Allocator.Temp);
                foreach (var entry in entries)
                {
                    var definition = entry.Definition.Value;
                    if (definition != null) enemyManager.SpawnGroup(definition, spawner.Position, entry.Count, spawner.SpawnRadius, riseIn: true);
                }
                entries.Dispose();
            }
        }
    }
}
