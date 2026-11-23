using Unity.Entities;
using Unity.Mathematics;

namespace LevelGeneration
{
    /// <summary>A baked level layout waiting to be built (LevelGenerationSystem). One per LevelLayoutAuthoring.</summary>
    public struct LevelLayout : IComponentData
    {
        public BlobAssetReference<LevelLayoutBlob> Blob;
        public BlobAssetReference<LevelGeometryBlob> Geometry;
        public uint Seed;
        public bool RandomSeed;
        public bool UseSaved;
        public int MaxAttempts;
        /// <summary>Chunks are built when the player comes within this many meters (LevelGenerationSystem).</summary>
        public float LoadDistance;
        public int ChunksPerFrame;
    }

    public struct LevelLayoutBlob
    {
        public int Width, Height;
        public float TileSize;
        /// <summary>World position of the grid's south-west corner.</summary>
        public float3 Origin;
        /// <summary>The grid's turn around Origin (yaw only).</summary>
        public quaternion Rotation;
        /// <summary>Cell the player spawns in.</summary>
        public int2 PlayerStart;
        /// <summary>Bend settings; its Seed is set when the level is built (the solve seed).</summary>
        public LevelDeformation Deformation;

        // Solver input (WfcCompiledLayout)
        public BlobArray<byte> VariantSlots;
        public BlobArray<float> VariantWeights;
        public BlobArray<int> VariantTile;
        public BlobArray<int> VariantRotation;
        public BlobArray<byte> VariantMirror;
        public BlobArray<byte> VariantWalkThrough;
        public BlobArray<ulong> CornerMasks;
        public BlobArray<ulong> EdgeMasks;
        public BlobArray<int> InitialCells;
        // Connectivity repair (WfcSolver.Repair)
        public bool Connect;
        public int StartCorner;
        public int FillPocketsSmallerThan;
        public ulong WalkableCorners, NotWalkableCorners, WalkableEdges;
        public BlobArray<byte> LockedCorners;

        // The finished level for gameplay (LevelSurface): per corner connection its terrain and height, per edge
        // connection whether it can be walked across, per tile its 4 corner and 4 edge connection indices
        public BlobArray<byte> CornerTerrains;
        public BlobArray<float> CornerHeights;
        public BlobArray<byte> EdgeWalkables;
        public BlobArray<byte> TileCorners;
        public BlobArray<byte> TileEdges;
        public BlobArray<byte> TileWalkThrough;
        /// <summary>How high each tile and piece model is placed: its lowest corner's height.</summary>
        public BlobArray<float> TileRaises;
        public BlobArray<float> PieceRaises;

        /// <summary>Cells the background ring reaches past the level on each side; 0 when there is none.</summary>
        public int RingMargin;
        /// <summary>The background ring's solver input (WfcCompiledLayout.Ring), on a grid RingMargin bigger on every side.</summary>
        public WfcSolveInput Ring;

        /// <summary>Landmarks placed by seed before solving (WfcLayoutCompiler.PlaceLandmarks), and the cells they stay off.</summary>
        public BlobArray<LevelLandmarkBlob> Landmarks;
        public BlobArray<int2> KeepClear;

        public BlobArray<LevelPiecePlacement> Pieces;
        /// <summary>Saved layout (LevelLayoutAuthoring.Pack per cell), empty when there is none.</summary>
        public BlobArray<int> SavedCells;

        // Procedural placement (LevelPlacementSet)
        public BlobArray<LevelPlacementRuleBlob> PlacementRules;
        public BlobArray<LevelPlacementGroupBlob> PlacementGroups;
        // Hand-placed prefabs (LevelLayoutAuthoring.Prefabs)
        public BlobArray<LevelPrefabPlacement> Prefabs;
    }

    /// <summary>A landmark (LandmarkPool) kept in a blob.</summary>
    public struct LevelLandmarkBlob
    {
        public int MinCount, MaxCount;
        public float Spacing;
        public BlobArray<LevelLandmarkOptionBlob> Options;
    }

    /// <summary>A landmark's piece in one rotation (LandmarkOption): footprint and corner connection indices.</summary>
    public struct LevelLandmarkOptionBlob
    {
        public int Piece, Rotation, Width, Height;
        public BlobArray<byte> Corners;
    }

    /// <summary>A solver input kept in a blob: what a WfcCompiledLayout holds for WfcSolveJob.</summary>
    public struct WfcSolveInput
    {
        public int Width, Height;
        public BlobArray<byte> VariantSlots;
        public BlobArray<float> VariantWeights;
        public BlobArray<int> VariantTile;
        public BlobArray<int> VariantRotation;
        public BlobArray<byte> VariantMirror;
        public BlobArray<byte> VariantWalkThrough;
        public BlobArray<ulong> CornerMasks;
        public BlobArray<ulong> EdgeMasks;
        public BlobArray<int> InitialCells;
    }

    /// <summary>A prefab painted on a cell, spawned at the cell's center when its chunk is built.</summary>
    public struct LevelPrefabPlacement
    {
        public int2 Cell;
        public int Prefab;              // Index into the LevelPlacementPrefab buffer
        public int Rotation;            // Quarter turns, with the bent grid's local direction
        public float ReservedRadius;    // Meters procedural placements keep away from the cell's center
    }

    public struct LevelPlacementRuleBlob
    {
        public PlacementRuleData Data;
        public int Prefab;          // Index into the LevelPlacementPrefab buffer, or -1
        public float TriggerRadius, SpawnRadius;
        public int GroupStart, GroupCount;
    }

    public struct LevelPlacementGroupBlob
    {
        public float Weight;
        public int EnemyStart, EnemyCount;  // Range in the LevelPlacementEnemy buffer
    }

    /// <summary>Baked prefabs of placement rules (LevelPlacementRuleBlob.Prefab) and hand-placed prefabs (LevelPrefabPlacement.Prefab).</summary>
    public struct LevelPlacementPrefab : IBufferElementData
    {
        public Entity Value;
    }

    /// <summary>The layout's backdrop (LevelBackdrop), when it has one; its silhouettes are LevelBackdropPiece.</summary>
    public struct LevelBackdropData : IComponentData
    {
        public float4 AbyssColor;
        public float FadeStart, FadeDepth;
        public int FadeBands;
        public Entity Mist;
        public int PieceCount;
        public float2 PieceDistance, PieceDepth, PieceScale;
        public float PieceSpacing;
    }

    public struct LevelBackdropPiece : IBufferElementData
    {
        public Entity Value;
    }

    /// <summary>The layout's VFX graph of its air and water (LevelAmbientGraphView shows it), when it has one.</summary>
    public struct LevelAmbientGraph : IComponentData
    {
        public UnityObjectRef<UnityEngine.VFX.VisualEffectAsset> Graph;
    }

    /// <summary>A kind of small animal the level is home to (AmbientLifeSet), on the layout entity.</summary>
    public struct LevelAmbientCreature : IBufferElementData
    {
        public Entity Prefab;
        public AmbientLifeSet.Kind Kind;
        public int Count;
        public float FleeRadius, Speed, Height;
        public float2 Scale;
    }

    /// <summary>
    /// One small animal, moved by AmbientCreatureSystem: it rests, wanders near Home and flees from players, going From
    /// To over Duration seconds (Elapsed so far).
    /// </summary>
    public struct AmbientCreature : IComponentData
    {
        public enum State : byte { Resting, Moving, Fleeing }

        public AmbientLifeSet.Kind Kind;
        public State Now;
        public float3 Home, From, To;
        public float Elapsed, Duration, Rest;
        public float Yaw;           // Radians, the way it faces
        public float FleeRadius, Speed;
        public Unity.Mathematics.Random Random;
    }

    /// <summary>Open ground where birds land (the level's corners with walkable ground all around), on the layout entity.</summary>
    public struct AmbientPerch : IBufferElementData
    {
        public float3 Position;
    }

    /// <summary>The enemies of placement rules' groups (LevelPlacementGroupBlob.EnemyStart).</summary>
    public struct LevelPlacementEnemy : IBufferElementData
    {
        public UnityObjectRef<Enemies.EnemyDefinition> Definition;
        public int Count;
    }

    public struct LevelPiecePlacement
    {
        public int Piece, Rotation;
        public int2 Cell, Size;
    }

    /// <summary>On a layout entity once its level is built. LevelReady follows a frame later.</summary>
    public struct LevelSpawned : IComponentData
    {
        public bool ReadyAnnounced;
        /// <summary>Center of the player start cell on the bent level.</summary>
        public float3 PlayerStart;
    }

    /// <summary>
    /// Singleton: the level's tiles exist and their colliders have world transforms. The player and enemies wait for it
    /// (see LevelState) so nothing spawns over empty space.
    /// </summary>
    public struct LevelReady : IComponentData
    {
        /// <summary>Center of the player start cell, at the grid's height.</summary>
        public float3 PlayerStart;
    }

    public static class LevelState
    {
        /// <summary>
        /// True once every subscene set to load has loaded, and the level (if one of them has a layout) has been built.
        /// Subscenes finish at different times: without the first check, the core subscene alone looks like a scene without a level.
        /// </summary>
        public static bool IsReady(EntityManager entityManager)
        {
            using var scenes = entityManager.CreateEntityQuery(ComponentType.ReadOnly<SceneReference>(), ComponentType.ReadOnly<RequestSceneLoaded>());
            using var sceneEntities = scenes.ToEntityArray(Unity.Collections.Allocator.Temp);
            foreach (var scene in sceneEntities)
                if (!Unity.Scenes.SceneSystem.IsSceneLoaded(entityManager.WorldUnmanaged, scene)) return false;

            using var layouts = entityManager.CreateEntityQuery(ComponentType.ReadOnly<LevelLayout>());
            if (layouts.IsEmpty) return true;
            using var ready = entityManager.CreateEntityQuery(ComponentType.ReadOnly<LevelReady>());
            return !ready.IsEmpty;
        }

        /// <summary>Where the player starts in the built level; false when there is no level (or it isn't built yet).</summary>
        public static bool TryGetPlayerStart(EntityManager entityManager, out float3 playerStart)
        {
            playerStart = default;
            using var ready = entityManager.CreateEntityQuery(ComponentType.ReadOnly<LevelReady>());
            if (ready.IsEmpty) return false;
            playerStart = ready.GetSingleton<LevelReady>().PlayerStart;
            return true;
        }
    }

    /// <summary>
    /// An enemy group from the level layout (LevelLayoutAuthoring.EnemySpawners). LevelEnemySpawnerSystem spawns it once, when the
    /// player first comes within TriggerRadius.
    /// </summary>
    public struct LevelEnemySpawner : IComponentData
    {
        public int2 Cell;
        /// <summary>World position of the cell's center on the bent level; set when the level is built.</summary>
        public float3 Position;
        public float TriggerRadius;
        public float SpawnRadius;
        public bool Triggered;
    }

    public struct LevelEnemySpawnerEntry : IBufferElementData
    {
        public UnityObjectRef<Enemies.EnemyDefinition> Definition;
        public int Count;
    }
}
