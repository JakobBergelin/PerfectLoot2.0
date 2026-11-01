using Unity.Entities;
using Unity.Mathematics;

/// <summary>What a scene with a LevelLayout is: a town (items and skills can be changed) or a dungeon (DungeonManager runs it).</summary>
public enum AreaKind
{
    Town,
    Dungeon,
}

/// <summary>On the level layout entity (LevelLayoutAuthoring.Kind / Dungeon). Read through GameArea.</summary>
public struct LevelArea : IComponentData
{
    public AreaKind Kind;
    public UnityObjectRef<DungeonDefinition> Dungeon;
    /// <summary>DungeonDefinition.ReturnPortal: spawned next to the player when the dungeon is finished.</summary>
    public Entity ReturnPortal;
}

/// <summary>
/// Singleton while a dungeon run is going (DungeonManager makes it). Enemy deaths are recorded in its DungeonKill buffer
/// (EnemyKillSystem), which DungeonManager reads and clears.
/// </summary>
public struct DungeonRun : IComponentData
{
}

public struct DungeonKill : IBufferElementData
{
    public float3 Position;
    public bool Boss;
}

/// <summary>An enemy whose death is already in the DungeonKill buffer (dying enemies can stay at 0 health for a while).</summary>
public struct KillCounted : IComponentData
{
}

/// <summary>The boss of a Kill Boss step (DungeonManager spawns it). Its death doesn't count toward Kill Enemies steps.</summary>
public struct DungeonBoss : IComponentData
{
}

/// <summary>A painted exit (DungeonExitAuthoring): reaching it finishes a Reach Exit step.</summary>
public struct DungeonExit : IComponentData
{
    public float Range;     // Meters, measured along the ground
}

/// <summary>A painted boss spawn point (DungeonBossSpawnAuthoring): where Kill Boss steps spawn their boss.</summary>
public struct DungeonBossSpawnPoint : IComponentData
{
}
