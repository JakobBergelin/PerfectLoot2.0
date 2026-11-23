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
            BakeArea(authoring);

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
            Copy(builder, ref root.VariantSlots, compiled.VariantSlots());
            Copy(builder, ref root.VariantWeights, compiled.VariantWeights());
            Copy(builder, ref root.VariantWalkThrough, compiled.VariantWalkThrough());
            var tiles = builder.Allocate(ref root.VariantTile, variants);
            var rotations = builder.Allocate(ref root.VariantRotation, variants);
            var mirrors = builder.Allocate(ref root.VariantMirror, variants);
            for (int v = 0; v < variants; v++)
            {
                tiles[v] = compiled.Variants[v].Tile;
                rotations[v] = compiled.Variants[v].Rotation;
                mirrors[v] = (byte)(compiled.Variants[v].Mirror ? 1 : 0);
            }

            Copy(builder, ref root.CornerMasks, compiled.CornerMasks);
            Copy(builder, ref root.EdgeMasks, compiled.EdgeMasks);
            Copy(builder, ref root.InitialCells, compiled.InitialCells);
            root.Connect = authoring.ConnectWalkableAreas;
            root.StartCorner = compiled.StartCorner;
            root.FillPocketsSmallerThan = authoring.FillPocketsSmallerThan;
            root.WalkableCorners = compiled.WalkableCorners;
            root.NotWalkableCorners = compiled.NotWalkableCorners;
            root.WalkableEdges = compiled.WalkableEdges;
            Copy(builder, ref root.LockedCorners, compiled.LockedCorners);

            Copy(builder, ref root.CornerTerrains, compiled.CornerTerrains);
            Copy(builder, ref root.CornerHeights, compiled.CornerHeights);
            Copy(builder, ref root.EdgeWalkables, compiled.EdgeWalkables);
            Copy(builder, ref root.TileCorners, tileSet.TileCornerIndices());
            Copy(builder, ref root.TileEdges, tileSet.TileEdgeIndices());
            Copy(builder, ref root.TileWalkThrough, tileSet.TileWalkThrough());
            Copy(builder, ref root.TileRaises, tileSet.TileRaises());
            Copy(builder, ref root.PieceRaises, tileSet.PieceRaises());

            var ring = compiled.Ring;
            root.RingMargin = ring?.Margin ?? 0;
            root.Ring.Width = ring?.Width ?? 0;
            root.Ring.Height = ring?.Height ?? 0;
            Copy(builder, ref root.Ring.VariantSlots, ring?.VariantSlots() ?? new byte[0]);
            Copy(builder, ref root.Ring.VariantWeights, ring?.VariantWeights() ?? new float[0]);
            Copy(builder, ref root.Ring.VariantWalkThrough, ring?.VariantWalkThrough() ?? new byte[0]);
            Copy(builder, ref root.Ring.VariantTile, ring?.Variants.ConvertAll(v => v.Tile).ToArray() ?? new int[0]);
            Copy(builder, ref root.Ring.VariantRotation, ring?.Variants.ConvertAll(v => v.Rotation).ToArray() ?? new int[0]);
            Copy(builder, ref root.Ring.VariantMirror, ring?.Variants.ConvertAll(v => (byte)(v.Mirror ? 1 : 0)).ToArray() ?? new byte[0]);
            Copy(builder, ref root.Ring.CornerMasks, ring?.CornerMasks ?? new ulong[0]);
            Copy(builder, ref root.Ring.EdgeMasks, ring?.EdgeMasks ?? new ulong[0]);
            Copy(builder, ref root.Ring.InitialCells, ring?.InitialCells ?? new int[0]);

            var landmarks = builder.Allocate(ref root.Landmarks, compiled.Landmarks.Count);
            for (int i = 0; i < compiled.Landmarks.Count; i++)
            {
                var pool = compiled.Landmarks[i];
                landmarks[i].MinCount = pool.MinCount;
                landmarks[i].MaxCount = pool.MaxCount;
                landmarks[i].Spacing = pool.Spacing;
                var options = builder.Allocate(ref landmarks[i].Options, pool.Options.Count);
                for (int o = 0; o < pool.Options.Count; o++)
                {
                    var option = pool.Options[o];
                    options[o].Piece = option.Piece;
                    options[o].Rotation = option.Rotation;
                    options[o].Width = option.Width;
                    options[o].Height = option.Height;
                    Copy(builder, ref options[o].Corners, System.Array.ConvertAll(option.Corners, c => (byte)c));
                }
            }
            Copy(builder, ref root.KeepClear, compiled.KeepClear.ConvertAll(c => new int2(c.x, c.y)).ToArray());

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
                if ((rule.Kind == PlacementKind.Prop || rule.Kind == PlacementKind.EdgeProp) && rule.Prefab != null)
                {
                    prefab = placementPrefabs.IndexOf(rule.Prefab);
                    if (prefab < 0) { prefab = placementPrefabs.Count; placementPrefabs.Add(rule.Prefab); }
                }
                ruleBlobs[r] = new LevelPlacementRuleBlob
                {
                    Data = rule.ToData(tileSet),
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

            // Hand-placed prefabs share the placement prefab buffer (the compiler warns about the ones skipped here)
            var placedPrefabs = authoring.Prefabs.FindAll(p => p != null && p.Prefab != null && authoring.InGrid(p.Cell));
            var prefabBlobs = builder.Allocate(ref root.Prefabs, placedPrefabs.Count);
            for (int i = 0; i < placedPrefabs.Count; i++)
            {
                var placed = placedPrefabs[i];
                int prefab = placementPrefabs.IndexOf(placed.Prefab);
                if (prefab < 0) { prefab = placementPrefabs.Count; placementPrefabs.Add(placed.Prefab); }
                prefabBlobs[i] = new LevelPrefabPlacement
                {
                    Cell = new int2(placed.Cell.x, placed.Cell.y), Prefab = prefab,
                    Rotation = ((placed.Rotation % 4) + 4) % 4, ReservedRadius = math.max(0f, placed.ReservedRadius),
                };
            }

            var blob = builder.CreateBlobAssetReference<LevelLayoutBlob>(Allocator.Persistent);
            builder.Dispose();
            AddBlobAsset(ref blob, out _);

            // Tile and piece art: stretched meshes and colliders, plus props (spawned whole)
            var materials = new List<Material>();
            var propPrefabs = new List<GameObject>();
            var geometryWarnings = new List<string>();
            var tileParts = new List<LevelGeometryExtractor.Part>();
            var lookStarts = new List<int>();
            foreach (var tile in tileSet.Tiles)
            {
                lookStarts.Add(tileParts.Count);
                for (int look = 0; look < (tile?.LookCount ?? 1); look++) tileParts.Add(Extract(tile?.Look(look), materials, geometryWarnings));
            }
            lookStarts.Add(tileParts.Count);
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
            Copy(geometryBuilder, ref geometryRoot.TileLookStart, lookStarts.ToArray());
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
            var backdrop = authoring.Backdrop;
            if (backdrop != null)
            {
                DependsOn(backdrop);
                var color = backdrop.AbyssColor.linear;
                AddComponent(entity, new LevelBackdropData
                {
                    AbyssColor = new float4(color.r, color.g, color.b, 1f),
                    FadeStart = backdrop.FadeStart, FadeDepth = backdrop.FadeDepth, FadeBands = backdrop.FadeBands,
                    Mist = backdrop.Mist != null ? GetEntity(backdrop.Mist, TransformUsageFlags.Dynamic) : Entity.Null,
                    PieceCount = backdrop.PieceCount, PieceDistance = backdrop.PieceDistance, PieceDepth = backdrop.PieceDepth,
                    PieceScale = backdrop.PieceScale, PieceSpacing = backdrop.PieceSpacing,
                });
                var pieceBuffer = AddBuffer<LevelBackdropPiece>(entity);
                foreach (var piece in backdrop.Pieces)
                    if (piece != null) pieceBuffer.Add(new LevelBackdropPiece { Value = GetEntity(piece, TransformUsageFlags.Dynamic) });
            }
            if (authoring.AmbientLife != null)
            {
                DependsOn(authoring.AmbientLife);
                var creatures = AddBuffer<LevelAmbientCreature>(entity);
                foreach (var creature in authoring.AmbientLife.Creatures)
                    if (creature?.Prefab != null && creature.Count > 0)
                        creatures.Add(new LevelAmbientCreature
                        {
                            Prefab = GetEntity(creature.Prefab, TransformUsageFlags.Dynamic), Kind = creature.Kind, Count = creature.Count,
                            FleeRadius = creature.FleeRadius, Speed = creature.Speed, Height = creature.Height, Scale = creature.Scale,
                        });
            }
            if (authoring.AmbientGraph != null) AddComponent(entity, new LevelAmbientGraph { Graph = authoring.AmbientGraph });
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

            // The level's enemy list (EnemyManager looks in this and the core EnemyPrefabs): every enemy it can spawn
            var enemyPrefabs = AddBuffer<EnemyPrefabElement>(entity);
            var seen = new HashSet<string>();
            var used = new List<Enemies.EnemyDefinition>();
            foreach (var entry in placementEnemies) used.Add(entry.Definition);
            foreach (var marker in authoring.EnemySpawners)
                if (marker != null)
                    foreach (var entry in marker.Group)
                        if (entry?.Definition != null && entry.Count > 0) used.Add(entry.Definition);
            // The dungeon's bosses and Survive waves, spawned by DungeonManager
            if (authoring.Kind == AreaKind.Dungeon && authoring.Dungeon != null)
                foreach (var step in authoring.Dungeon.Steps)
                {
                    if (step == null) continue;
                    if (step.Kind == DungeonStepKind.KillBoss && step.Boss != null)
                    {
                        DependsOn(step.Boss);
                        used.Add(step.Boss);
                    }
                    if (step.Kind != DungeonStepKind.Survive) continue;
                    foreach (var wave in step.Waves)
                        if (wave != null)
                            foreach (var entry in wave.Enemies)
                                if (entry?.Definition != null && entry.Count > 0)
                                {
                                    DependsOn(entry.Definition);
                                    used.Add(entry.Definition);
                                }
                }
            foreach (var definition in used)
            {
                if (!seen.Add(definition.name)) continue;
                if (definition.Prefab == null)
                {
                    Debug.LogWarning($"Level layout: enemy {definition.name} has no Prefab, so it can't spawn.", authoring);
                    continue;
                }
                enemyPrefabs.Add(new EnemyPrefabElement
                {
                    Key = definition.name,
                    Prefab = GetEntity(definition.Prefab, TransformUsageFlags.Dynamic),
                    Definition = definition,
                });
            }
        }

        // Town or dungeon (GameArea); a dungeon also gets its return portal prefab
        void BakeArea(LevelLayoutAuthoring authoring)
        {
            var dungeon = authoring.Kind == AreaKind.Dungeon ? authoring.Dungeon : null;
            DependsOn(dungeon);
            if (authoring.Kind == AreaKind.Dungeon && dungeon == null)
                Debug.LogWarning("Level layout is a Dungeon without a Dungeon, so it has no steps and can't be finished.", authoring);

            var area = new LevelArea { Kind = authoring.Kind, Dungeon = dungeon };
            if (dungeon != null)
            {
                foreach (var step in dungeon.Steps)
                {
                    if (step != null && step.Kind == DungeonStepKind.KillBoss && step.Boss == null)
                        Debug.LogWarning($"Dungeon {dungeon.name} has a Kill Boss step without a Boss; it's skipped.", dungeon);
                    if (step != null && step.Kind == DungeonStepKind.Survive && !step.Waves.Exists(wave => wave != null && wave.Weight > 0f && wave.Enemies.Count > 0))
                        Debug.LogWarning($"Dungeon {dungeon.name} has a Survive step without waves; nothing comes.", dungeon);
                }
                DependsOn(dungeon.ReturnPortal);
                if (dungeon.ReturnPortal != null) area.ReturnPortal = GetEntity(dungeon.ReturnPortal, TransformUsageFlags.Dynamic);
                else Debug.LogWarning($"Dungeon {dungeon.name} has no Return Portal, so finishing it opens no way back.", dungeon);
            }
            AddComponent(GetEntity(TransformUsageFlags.None), area);
        }

        static void Copy<T>(BlobBuilder builder, ref BlobArray<T> target, T[] source) where T : unmanaged
        {
            var array = builder.Allocate(ref target, source.Length);
            for (int i = 0; i < source.Length; i++) array[i] = source[i];
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
