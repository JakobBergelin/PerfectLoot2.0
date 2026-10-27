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
        public BlobArray<byte> VariantCorners;
        public BlobArray<float> VariantWeights;
        public BlobArray<int> VariantTile;
        public BlobArray<int> VariantRotation;
        public BlobArray<byte> CornerMasks;
        public BlobArray<int> InitialCells;
        // Connectivity repair (WfcSolver.Repair)
        public bool Connect;
        public int StartCorner;
        public int FillPocketsSmallerThan;
        public BlobArray<byte> LockedCorners;

        public BlobArray<LevelPiecePlacement> Pieces;
        /// <summary>Saved layout (Tile * 4 + Rotation per cell), empty when there is none.</summary>
        public BlobArray<int> SavedCells;

        /// <summary>4 terrains per tile (SW, SE, NE, NW), to know every corner's terrain from the placed tiles.</summary>
        public BlobArray<byte> TileCorners;
        // Procedural placement (LevelPlacementSet)
        public BlobArray<LevelPlacementRuleBlob> PlacementRules;
        public BlobArray<LevelPlacementGroupBlob> PlacementGroups;
        // Hand-placed prefabs (LevelLayoutAuthoring.Prefabs)
        public BlobArray<LevelPrefabPlacement> Prefabs;
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
        /// <summary>True when there's no level layout to wait for, or the level has been built.</summary>
        public static bool IsReady(EntityManager entityManager)
        {
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
