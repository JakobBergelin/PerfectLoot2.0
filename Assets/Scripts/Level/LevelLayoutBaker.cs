using System.Collections.Generic;
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
            root.Deformation = authoring.Deformation(authoring.Seed);

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
            root.Connect = authoring.ConnectWalkableAreas;
            root.StartCorner = compiled.StartCorner;
            root.FillPocketsSmallerThan = authoring.FillPocketsSmallerThan;
            var lockedCorners = builder.Allocate(ref root.LockedCorners, compiled.LockedCorners.Length);
            for (int i = 0; i < compiled.LockedCorners.Length; i++) lockedCorners[i] = compiled.LockedCorners[i];

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

            var tileCorners = builder.Allocate(ref root.TileCorners, tileSet.Tiles.Count * 4);
            for (int t = 0; t < tileSet.Tiles.Count; t++)
            {
                var terrain = tileSet.Tiles[t]?.Corners;
                for (int k = 0; k < 4; k++) tileCorners[t * 4 + k] = terrain != null ? (byte)terrain[k] : LevelPlacement.Unknown;
            }

            // Placement rules; their prefabs and enemies go in buffers below
            var placementSet = authoring.Placements;
            DependsOn(placementSet);
            var rules = placementSet != null ? placementSet.Rules : new List<PlacementRule>();
            var placementPrefabs = new List<GameObject>();
            var placementEnemies = new List<EnemySpawner.Entry>();
            var ruleBlobs = builder.Allocate(ref root.PlacementRules, rules.Count);
            int groupCount = 0;
            foreach (var rule in rules) groupCount += rule?.Kind == PlacementKind.EnemySpawner ? rule.Groups.Count : 0;
            var groupBlobs = builder.Allocate(ref root.PlacementGroups, groupCount);
            int group = 0;
            for (int r = 0; r < rules.Count; r++)
            {
                var rule = rules[r];
                if (rule == null) { ruleBlobs[r] = new LevelPlacementRuleBlob { Prefab = -1 }; continue; }
                int prefab = -1;
                if (rule.Kind == PlacementKind.Prop && rule.Prefab != null)
                {
                    prefab = placementPrefabs.IndexOf(rule.Prefab);
                    if (prefab < 0) { prefab = placementPrefabs.Count; placementPrefabs.Add(rule.Prefab); }
                }
                ruleBlobs[r] = new LevelPlacementRuleBlob
                {
                    Data = new PlacementRuleData
                    {
                        Kind = rule.Kind, Spacing = rule.Spacing, ClearRadius = rule.ClearRadius,
                        MinDistanceFromStart = rule.MinDistanceFromStart, MaxSlope = rule.MaxSlope,
                    },
                    Prefab = prefab,
                    TriggerRadius = rule.TriggerRadius,
                    SpawnRadius = rule.SpawnRadius,
                    GroupStart = group,
                    GroupCount = rule.Kind == PlacementKind.EnemySpawner ? rule.Groups.Count : 0,
                };
                if (rule.Kind != PlacementKind.EnemySpawner) continue;
                foreach (var enemyGroup in rule.Groups)
                {
                    int start = placementEnemies.Count;
                    if (enemyGroup != null)
                        foreach (var entry in enemyGroup.Enemies)
                            if (entry?.Definition != null && entry.Count > 0) placementEnemies.Add(entry);
                    groupBlobs[group++] = new LevelPlacementGroupBlob
                    {
                        Weight = enemyGroup != null ? enemyGroup.Weight : 0f,
                        EnemyStart = start, EnemyCount = placementEnemies.Count - start,
                    };
                }
            }

            var blob = builder.CreateBlobAssetReference<LevelLayoutBlob>(Allocator.Persistent);
            builder.Dispose();
            AddBlobAsset(ref blob, out _);

            // Tile and piece art: stretched meshes and colliders, plus props (spawned whole)
            var materials = new List<Material>();
            var propPrefabs = new List<GameObject>();
            var geometryWarnings = new List<string>();
            var tileParts = new List<LevelGeometryExtractor.Part>();
            foreach (var tile in tileSet.Tiles) tileParts.Add(Extract(tile?.Prefab, materials, geometryWarnings));
            var pieceParts = new List<LevelGeometryExtractor.Part>();
            foreach (var piece in tileSet.Pieces) pieceParts.Add(Extract(piece?.Prefab, materials, geometryWarnings));
            foreach (var warning in geometryWarnings) Debug.LogWarning($"Level layout: {warning}", authoring);

            int PropIndex(GameObject prefab)
            {
                int index = propPrefabs.IndexOf(prefab);
                if (index < 0) { index = propPrefabs.Count; propPrefabs.Add(prefab); }
                return index;
            }

            var geometryBuilder = new BlobBuilder(Allocator.Temp);
            ref var geometryRoot = ref geometryBuilder.ConstructRoot<LevelGeometryBlob>();
            var tileGeometry = geometryBuilder.Allocate(ref geometryRoot.Tiles, tileParts.Count);
            for (int i = 0; i < tileParts.Count; i++) LevelGeometryExtractor.Write(geometryBuilder, ref tileGeometry[i], tileParts[i], PropIndex);
            var pieceGeometry = geometryBuilder.Allocate(ref geometryRoot.Pieces, pieceParts.Count);
            for (int i = 0; i < pieceParts.Count; i++) LevelGeometryExtractor.Write(geometryBuilder, ref pieceGeometry[i], pieceParts[i], PropIndex);
            var geometry = geometryBuilder.CreateBlobAssetReference<LevelGeometryBlob>(Allocator.Persistent);
            geometryBuilder.Dispose();
            AddBlobAsset(ref geometry, out _);

            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new LevelLayout
            {
                Blob = blob,
                Geometry = geometry,
                Seed = authoring.Seed,
                RandomSeed = authoring.RandomSeedEachPlay,
                UseSaved = saved != null,
                MaxAttempts = authoring.MaxAttempts,
                LoadDistance = authoring.LoadDistance,
                ChunksPerFrame = authoring.ChunksPerFrame,
            });

            var materialBuffer = AddBuffer<LevelMaterial>(entity);
            foreach (var material in materials) materialBuffer.Add(new LevelMaterial { Value = material });
            var propBuffer = AddBuffer<LevelPropPrefab>(entity);
            foreach (var prefab in propPrefabs) propBuffer.Add(new LevelPropPrefab { Value = GetEntity(prefab, TransformUsageFlags.Dynamic) });
            var placementPrefabBuffer = AddBuffer<LevelPlacementPrefab>(entity);
            foreach (var prefab in placementPrefabs)
            {
                DependsOn(prefab);
                placementPrefabBuffer.Add(new LevelPlacementPrefab { Value = GetEntity(prefab, TransformUsageFlags.Dynamic) });
            }
            var placementEnemyBuffer = AddBuffer<LevelPlacementEnemy>(entity);
            foreach (var entry in placementEnemies)
            {
                DependsOn(entry.Definition);
                placementEnemyBuffer.Add(new LevelPlacementEnemy { Definition = entry.Definition, Count = entry.Count });
            }

            // Enemy spawners: one entity each, at its cell (the position on the bent level is set when it's built)
            foreach (var marker in authoring.EnemySpawners)
            {
                if (marker == null) continue;
                var spawner = CreateAdditionalEntity(TransformUsageFlags.None, entityName: $"EnemySpawner {marker.Cell}");
                AddComponent(spawner, new LevelEnemySpawner
                {
                    Cell = new int2(marker.Cell.x, marker.Cell.y),
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

        LevelGeometryExtractor.Part Extract(GameObject prefab, List<Material> materials, List<string> warnings)
        {
            if (prefab == null) return new LevelGeometryExtractor.Part();
            DependsOn(prefab);
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true)) DependsOn(filter.sharedMesh);
            return LevelGeometryExtractor.Extract(prefab, materials, warnings);
        }
    }
}
