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
    /// (LevelDeformation, seeded like the solve), then streams the geometry in chunks of LevelMeshBuilder.ChunkSize cells:
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
            public Material[] Materials;
            public Entity[] PropPrefabs;
            public int ChunksX, ChunksY;
            public bool[] Built;
            public List<int>[] PiecesByChunk; // Piece placements, by the chunk of their south-west cell
            public float LoadDistance;
            public int ChunksPerFrame;
        }

        readonly List<LevelStream> _streams = new List<LevelStream>();
        readonly List<Mesh> _meshes = new List<Mesh>();
        readonly List<BlobAssetReference<Unity.Physics.Collider>> _colliders = new List<BlobAssetReference<Unity.Physics.Collider>>();

        protected override void OnCreate()
        {
            RequireForUpdate<LevelLayout>();
        }

        protected override void OnDestroy()
        {
            foreach (var collider in _colliders) if (collider.IsCreated) collider.Dispose();
            foreach (var mesh in _meshes) if (mesh != null) Object.Destroy(mesh);
            _colliders.Clear();
            _meshes.Clear();
            _streams.Clear();
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

            // Stream chunks around the player (before it spawns, the start's chunks are already built)
            if (_streams.Count == 0) return;
            float3? player = null;
            foreach (var transform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<Player>())
                player = transform.ValueRO.Position;
            if (player == null) return;

            for (int i = _streams.Count - 1; i >= 0; i--)
            {
                var stream = _streams[i];
                if (!EntityManager.Exists(stream.Layout) || !EntityManager.HasComponent<LevelLayout>(stream.Layout))
                {
                    _streams.RemoveAt(i);
                    continue;
                }
                StreamChunks(stream, player.Value, stream.ChunksPerFrame);
            }
        }

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
            int chunkSize = LevelMeshBuilder.ChunkSize;
            var stream = new LevelStream
            {
                Layout = layoutEntity,
                Packed = packed.ToArray(),
                Corners = deformation.BuildCorners(),
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
            _streams.Add(stream);

            // Spawners in this level: their cells' centers on the bent grid (known without their chunks)
            using var spawners = SystemAPI.QueryBuilder().WithAllRW<LevelEnemySpawner>().Build().ToEntityArray(Allocator.Temp);
            foreach (var entity in spawners)
            {
                var spawner = EntityManager.GetComponentData<LevelEnemySpawner>(entity);
                spawner.Position = GridToWorld(ref blob, stream.Corners, spawner.Cell.x + 0.5f, spawner.Cell.y + 0.5f);
                EntityManager.SetComponentData(entity, spawner);
            }

            // The start's surroundings, all at once: the player spawns on them
            var playerStart = GridToWorld(ref blob, stream.Corners, blob.PlayerStart.x + 0.5f, blob.PlayerStart.y + 0.5f);
            long chunksStart = timer.ElapsedMilliseconds;
            int built = StreamChunks(stream, playerStart, int.MaxValue);

            Debug.Log($"Level built: {blob.Width}x{blob.Height} cells of {blob.TileSize} m, seed {seed}. Solve {solveMs} ms; " +
                      $"first {built} of {stream.Built.Length} chunks {timer.ElapsedMilliseconds - chunksStart} ms; " +
                      $"the rest stream in within {stream.LoadDistance} m of the player.");
            return playerStart;
        }

        /// <summary>Builds up to budget unbuilt chunks within LoadDistance of focus, nearest first. Returns how many.</summary>
        int StreamChunks(LevelStream stream, float3 focus, int budget)
        {
            var layout = EntityManager.GetComponentData<LevelLayout>(stream.Layout);
            ref var blob = ref layout.Blob.Value;
            int chunkSize = LevelMeshBuilder.ChunkSize;
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
            if (candidates.Count == 0) return 0;
            candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            int count = math.min(budget, candidates.Count);
            for (int i = 0; i < count; i++)
                BuildChunk(stream, ref blob, ref layout.Geometry.Value, candidates[i].Chunk);
            return count;
        }

        void BuildChunk(LevelStream stream, ref LevelLayoutBlob blob, ref LevelGeometryBlob geometry, int chunk)
        {
            stream.Built[chunk] = true;
            int chunkSize = LevelMeshBuilder.ChunkSize;
            int cx = chunk % stream.ChunksX, cy = chunk / stream.ChunksX;
            var builder = new LevelMeshBuilder(stream.Corners, blob.Width, blob.Height, blob.TileSize);

            for (int y = cy * chunkSize; y < math.min(blob.Height, (cy + 1) * chunkSize); y++)
                for (int x = cx * chunkSize; x < math.min(blob.Width, (cx + 1) * chunkSize); x++)
                {
                    int cell = stream.Packed[x + y * blob.Width];
                    if (cell < 0) continue;
                    int tile = LevelLayoutAuthoring.PackedTile(cell);
                    if (tile >= geometry.Tiles.Length) continue;
                    builder.Add(ref geometry.Tiles[tile], new float2(x + 0.5f, y + 0.5f), LevelLayoutAuthoring.PackedRotation(cell), new int2(x, y));
                }

            if (stream.PiecesByChunk[chunk] != null)
                foreach (int i in stream.PiecesByChunk[chunk])
                {
                    var piece = blob.Pieces[i];
                    if (piece.Piece >= geometry.Pieces.Length) continue;
                    builder.Add(ref geometry.Pieces[piece.Piece], new float2(piece.Cell) + new float2(piece.Size) * 0.5f, piece.Rotation, piece.Cell);
                }

            builder.CreateEntities(EntityManager, blob.Origin, blob.Rotation, stream.Materials, _meshes, _colliders);
            foreach (var prop in builder.Props)
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
                Debug.Log($"Level generated with seed {seed}" + (attempts > 1 ? $" (attempt {attempts})." : "."));
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
            job.Attempts.Dispose();
            return attempts > 0;
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
