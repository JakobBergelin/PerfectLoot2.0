using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace LevelGeneration
{
    /// <summary>Bakes a LevelLayoutAuthoring into a LevelLayout request plus the tile and piece prefabs.</summary>
    public class LevelLayoutBaker : Baker<LevelLayoutAuthoring>
    {
        public override void Bake(LevelLayoutAuthoring authoring)
        {
            var tileSet = authoring.TileSet;
            DependsOn(tileSet);
            if (tileSet == null)
            {
                Debug.LogWarning("LevelLayoutAuthoring has no tile set, so no level is built.", authoring);
                return;
            }
            var transform = GetComponent<Transform>();

            var compiled = WfcLayoutCompiler.Compile(authoring);
            foreach (var warning in compiled.Warnings) Debug.LogWarning($"Level layout: {warning}", authoring);

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<LevelLayoutBlob>();
            root.Width = authoring.Width;
            root.Height = authoring.Height;
            root.TileSize = authoring.TileSize;
            root.Origin = transform.position;
            root.Rotation = authoring.Yaw;
            root.PlayerStart = new int2(authoring.PlayerStart.x, authoring.PlayerStart.y);

            int variants = compiled.Variants.Count;
            var corners = builder.Allocate(ref root.VariantCorners, variants * 4);
            var weights = builder.Allocate(ref root.VariantWeights, variants);
            var tiles = builder.Allocate(ref root.VariantTile, variants);
            var rotations = builder.Allocate(ref root.VariantRotation, variants);
            for (int v = 0; v < variants; v++)
            {
                var variant = compiled.Variants[v];
                for (int k = 0; k < 4; k++) corners[v * 4 + k] = (byte)variant.Corner(k);
                weights[v] = variant.Weight;
                tiles[v] = variant.Tile;
                rotations[v] = variant.Rotation;
            }

            var cornerMasks = builder.Allocate(ref root.CornerMasks, compiled.CornerMasks.Length);
            for (int i = 0; i < compiled.CornerMasks.Length; i++) cornerMasks[i] = compiled.CornerMasks[i];
            var initialCells = builder.Allocate(ref root.InitialCells, compiled.InitialCells.Length);
            for (int i = 0; i < compiled.InitialCells.Length; i++) initialCells[i] = compiled.InitialCells[i];

            var pieces = builder.Allocate(ref root.Pieces, compiled.Pieces.Count);
            for (int i = 0; i < compiled.Pieces.Count; i++)
            {
                var placement = compiled.Pieces[i];
                pieces[i] = new LevelPiecePlacement
                {
                    Piece = placement.Piece, Rotation = placement.Rotation,
                    Cell = new int2(placement.Cell.x, placement.Cell.y), Size = new int2(placement.Size.x, placement.Size.y),
                };
            }

            bool useSaved = authoring.Mode == LevelGenerationMode.UseSavedLayout;
            var saved = useSaved ? authoring.SavedCells : null;
            if (useSaved && saved == null)
                Debug.LogWarning("Level layout is set to Use Saved Layout but has none for this grid size; generating instead.", authoring);
            var savedCells = builder.Allocate(ref root.SavedCells, saved?.Length ?? 0);
            for (int i = 0; i < (saved?.Length ?? 0); i++) savedCells[i] = saved[i];

            var blob = builder.CreateBlobAssetReference<LevelLayoutBlob>(Allocator.Persistent);
            builder.Dispose();
            AddBlobAsset(ref blob, out _);

            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new LevelLayout
            {
                Blob = blob,
                Seed = authoring.Seed,
                RandomSeed = authoring.RandomSeedEachPlay,
                UseSaved = saved != null,
                MaxAttempts = authoring.MaxAttempts,
            });

            var tilePrefabs = AddBuffer<LevelTilePrefab>(entity);
            foreach (var tile in tileSet.Tiles)
            {
                if (tile?.Prefab != null) DependsOn(tile.Prefab);
                tilePrefabs.Add(new LevelTilePrefab { Value = tile?.Prefab != null ? GetEntity(tile.Prefab, TransformUsageFlags.Dynamic) : Entity.Null });
            }
            var piecePrefabs = AddBuffer<LevelPiecePrefab>(entity);
            foreach (var piece in tileSet.Pieces)
            {
                if (piece?.Prefab != null) DependsOn(piece.Prefab);
                piecePrefabs.Add(new LevelPiecePrefab { Value = piece?.Prefab != null ? GetEntity(piece.Prefab, TransformUsageFlags.Dynamic) : Entity.Null });
            }

            // Enemy spawners: one entity each, at its cell's center (GroundPlacement puts the enemies on the floor)
            foreach (var marker in authoring.EnemySpawners)
            {
                if (marker == null) continue;
                var spawner = CreateAdditionalEntity(TransformUsageFlags.None, entityName: $"EnemySpawner {marker.Cell}");
                AddComponent(spawner, new LevelEnemySpawner
                {
                    Position = authoring.CellCenter(marker.Cell.x, marker.Cell.y),
                    TriggerRadius = marker.TriggerRadius,
                    SpawnRadius = marker.SpawnRadius,
                });
                var entries = AddBuffer<LevelEnemySpawnerEntry>(spawner);
                foreach (var entry in marker.Group)
                {
                    if (entry?.Definition == null || entry.Count <= 0) continue;
                    DependsOn(entry.Definition);
                    entries.Add(new LevelEnemySpawnerEntry { Definition = entry.Definition, Count = entry.Count });
                }
            }
        }
    }
}
