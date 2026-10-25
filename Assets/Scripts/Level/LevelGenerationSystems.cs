using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace LevelGeneration
{
    /// <summary>
    /// Builds baked level layouts: solves them with WfcSolveJob (or takes the saved layout), instantiates the tile and
    /// piece prefabs, then announces LevelReady a frame later, once the tiles' child colliders have world transforms.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class LevelGenerationSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<LevelLayout>();
        }

        protected override void OnUpdate()
        {
            // Spawned last frame: transforms are calculated now, so the level is ready
            bool announce = false;
            var ready = new LevelReady();
            foreach (var (spawned, layout) in SystemAPI.Query<RefRW<LevelSpawned>, RefRO<LevelLayout>>())
            {
                if (spawned.ValueRO.ReadyAnnounced) continue;
                spawned.ValueRW.ReadyAnnounced = true;
                announce = true;
                ref var blob = ref layout.ValueRO.Blob.Value;
                ready.PlayerStart = GridToWorld(ref blob, blob.PlayerStart.x + 0.5f, blob.PlayerStart.y + 0.5f);
            }
            if (announce && !SystemAPI.HasSingleton<LevelReady>()) EntityManager.CreateSingleton(ready, "LevelReady");

            using var pending = SystemAPI.QueryBuilder().WithAll<LevelLayout>().WithNone<LevelSpawned>().Build().ToEntityArray(Allocator.Temp);
            foreach (var entity in pending)
            {
                Build(entity);
                EntityManager.AddComponentData(entity, new LevelSpawned());
            }
        }

        void Build(Entity layoutEntity)
        {
            var layout = EntityManager.GetComponentData<LevelLayout>(layoutEntity);
            ref var blob = ref layout.Blob.Value;
            int cells = blob.Width * blob.Height;

            // Per cell: Tile * 4 + Rotation, or negative for nothing
            var packed = new NativeArray<int>(cells, Allocator.Temp);
            if (layout.UseSaved && blob.SavedCells.Length == cells)
            {
                for (int i = 0; i < cells; i++) packed[i] = blob.SavedCells[i];
            }
            else if (!Solve(ref blob, layout, packed))
            {
                packed.Dispose();
                return;
            }

            // Copy the prefabs out: instantiating is a structural change that invalidates the buffers
            var tilePrefabs = EntityManager.GetBuffer<LevelTilePrefab>(layoutEntity).ToNativeArray(Allocator.Temp);
            var piecePrefabs = EntityManager.GetBuffer<LevelPiecePrefab>(layoutEntity).ToNativeArray(Allocator.Temp);

            int tiles = 0, pieces = 0, missingPrefabs = 0;
            for (int y = 0; y < blob.Height; y++)
                for (int x = 0; x < blob.Width; x++)
                {
                    int cell = packed[x + y * blob.Width];
                    if (cell < 0) continue;
                    int tile = LevelLayoutAuthoring.PackedTile(cell);
                    if (tile >= tilePrefabs.Length || tilePrefabs[tile].Value == Entity.Null) { missingPrefabs++; continue; }
                    var position = GridToWorld(ref blob, x + 0.5f, y + 0.5f);
                    Spawn(tilePrefabs[tile].Value, position, Turn(ref blob, LevelLayoutAuthoring.PackedRotation(cell)));
                    tiles++;
                }

            for (int i = 0; i < blob.Pieces.Length; i++)
            {
                var piece = blob.Pieces[i];
                if (piece.Piece >= piecePrefabs.Length || piecePrefabs[piece.Piece].Value == Entity.Null) { missingPrefabs++; continue; }
                var center = new float2(piece.Cell) + new float2(piece.Size) * 0.5f;
                var position = GridToWorld(ref blob, center.x, center.y);
                Spawn(piecePrefabs[piece.Piece].Value, position, Turn(ref blob, piece.Rotation));
                pieces++;
            }

            Debug.Log($"Level spawned: {tiles} tiles and {pieces} pieces from {blob.Origin} " +
                      $"({blob.Width}x{blob.Height} cells of {blob.TileSize} m)" +
                      (missingPrefabs > 0 ? $". {missingPrefabs} had no prefab and were skipped." : "."));

            tilePrefabs.Dispose();
            piecePrefabs.Dispose();
            packed.Dispose();
        }

        bool Solve(ref LevelLayoutBlob blob, LevelLayout layout, NativeArray<int> packed)
        {
            int cells = blob.Width * blob.Height;
            int variants = blob.VariantWeights.Length;
            uint seed = layout.RandomSeed ? (uint)System.DateTime.Now.Ticks : layout.Seed;

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

        /// <summary>World position of a grid point (in cells): turned around the origin with the layout.</summary>
        public static float3 GridToWorld(ref LevelLayoutBlob blob, float x, float y) =>
            blob.Origin + math.mul(blob.Rotation, new float3(x * blob.TileSize, 0f, y * blob.TileSize));

        // A tile's or piece's 90° steps, on top of the layout's turn
        static quaternion Turn(ref LevelLayoutBlob blob, int rotation) =>
            math.mul(blob.Rotation, quaternion.RotateY(math.radians(90f * rotation)));

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

            // Unity Physics bakes static colliders unparented, in the prefab's space (e.g. the placeholder blocks).
            // They don't follow the root, so move them the same way; parented parts already follow it.
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
