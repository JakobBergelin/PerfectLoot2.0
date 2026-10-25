using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace LevelGeneration
{
    /// <summary>
    /// Builds baked level layouts: solves them with WfcSolveJob (or takes the saved layout), bends the grid
    /// (LevelDeformation, seeded like the solve), builds the merged level meshes and colliders (LevelMeshBuilder),
    /// spawns the props, then announces LevelReady a frame later. Frees the meshes and colliders it made when destroyed.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class LevelGenerationSystem : SystemBase
    {
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
        }

        /// <summary>Builds the level; returns the player start on it.</summary>
        float3 Build(Entity layoutEntity)
        {
            var layout = EntityManager.GetComponentData<LevelLayout>(layoutEntity);
            ref var blob = ref layout.Blob.Value;
            int cells = blob.Width * blob.Height;
            bool useSaved = layout.UseSaved && blob.SavedCells.Length == cells;
            // A saved layout keeps the editor's seed, so its bend matches the preview too
            uint seed = useSaved || !layout.RandomSeed ? layout.Seed : (uint)System.DateTime.Now.Ticks;

            // Per cell: Tile * 4 + Rotation, or negative for nothing
            var packed = new NativeArray<int>(cells, Allocator.Temp);
            if (useSaved)
            {
                for (int i = 0; i < cells; i++) packed[i] = blob.SavedCells[i];
            }
            else if (!Solve(ref blob, layout, seed, packed))
            {
                for (int i = 0; i < cells; i++) packed[i] = WfcSolver.Free;
            }

            var deformation = blob.Deformation;
            deformation.Seed = seed;
            var corners = deformation.BuildCorners();
            var builder = new LevelMeshBuilder(corners, blob.Width, blob.Height, blob.TileSize);
            ref var geometry = ref layout.Geometry.Value;

            int tiles = 0, pieces = 0;
            for (int y = 0; y < blob.Height; y++)
                for (int x = 0; x < blob.Width; x++)
                {
                    int cell = packed[x + y * blob.Width];
                    if (cell < 0) continue;
                    int tile = LevelLayoutAuthoring.PackedTile(cell);
                    if (tile >= geometry.Tiles.Length) continue;
                    builder.Add(ref geometry.Tiles[tile], new float2(x + 0.5f, y + 0.5f), LevelLayoutAuthoring.PackedRotation(cell), new int2(x, y));
                    tiles++;
                }

            for (int i = 0; i < blob.Pieces.Length; i++)
            {
                var piece = blob.Pieces[i];
                if (piece.Piece >= geometry.Pieces.Length) continue;
                builder.Add(ref geometry.Pieces[piece.Piece], new float2(piece.Cell) + new float2(piece.Size) * 0.5f, piece.Rotation, piece.Cell);
                pieces++;
            }
            packed.Dispose();

            // Copied out first: creating entities is a structural change that invalidates the buffers
            var materialBuffer = EntityManager.GetBuffer<LevelMaterial>(layoutEntity);
            var materials = new Material[materialBuffer.Length];
            for (int i = 0; i < materials.Length; i++) materials[i] = materialBuffer[i].Value.Value;
            var propPrefabs = EntityManager.GetBuffer<LevelPropPrefab>(layoutEntity).ToNativeArray(Allocator.Temp);

            var (renderers, colliders) = builder.CreateEntities(EntityManager, blob.Origin, blob.Rotation, materials, _meshes, _colliders);
            int props = 0;
            foreach (var prop in builder.Props)
            {
                if (prop.Prefab < 0 || prop.Prefab >= propPrefabs.Length || propPrefabs[prop.Prefab].Value == Entity.Null) continue;
                Spawn(propPrefabs[prop.Prefab].Value, blob.Origin + math.mul(blob.Rotation, prop.Position), math.mul(blob.Rotation, prop.Rotation));
                props++;
            }
            propPrefabs.Dispose();

            // Spawners in this level: their cells' centers on the bent grid
            using var spawners = SystemAPI.QueryBuilder().WithAllRW<LevelEnemySpawner>().Build().ToEntityArray(Allocator.Temp);
            foreach (var entity in spawners)
            {
                var spawner = EntityManager.GetComponentData<LevelEnemySpawner>(entity);
                spawner.Position = GridToWorld(ref blob, corners, spawner.Cell.x + 0.5f, spawner.Cell.y + 0.5f);
                EntityManager.SetComponentData(entity, spawner);
            }

            Debug.Log($"Level built: {tiles} tiles, {pieces} pieces and {props} props as {renderers} meshes and {colliders} colliders " +
                      $"({blob.Width}x{blob.Height} cells of {blob.TileSize} m, seed {seed}).");
            return GridToWorld(ref blob, corners, blob.PlayerStart.x + 0.5f, blob.PlayerStart.y + 0.5f);
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
