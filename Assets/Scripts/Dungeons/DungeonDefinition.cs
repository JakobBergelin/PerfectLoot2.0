using System;
using System.Collections.Generic;
using Enemies;
using Loottables;
using UnityEngine;

public enum DungeonStepKind
{
    KillEnemies,    // Kill a share of the enemies the level's spawners make; dying costs time
    KillBoss,       // Kill a boss spawned when the step starts; dying costs time
    ReachExit,      // Reach a painted Dungeon Exit; dying sends the player back to the level's start
    Survive,        // Stay alive while waves of enemies come; dying fails the dungeon
}

/// <summary>One objective of a dungeon. A dungeon's steps are done in order; the last one finishes it.</summary>
[Serializable]
public class DungeonStep
{
    public DungeonStepKind Kind;
    [Tooltip("Kill Enemies: the share of all enemies from the level's spawners (counted since the dungeon started)")]
    [Range(1f, 100f)] public float KillPercent = 90f;
    [Tooltip("Kill Boss: spawned at a painted Boss Spawn Point, or near the player when the level has none")]
    public EnemyDefinition Boss;
    [Tooltip("Kill Boss: meters a player must come within the Boss Spawn Point before the boss appears there (0: at once). " +
             "On a long level the boss waits in its arena instead of on ground that hasn't streamed in yet")]
    [Min(0f)] public float BossTriggerRadius;

    [Header("Survive")]
    [Tooltip("Seconds to stay alive. Dying before then fails the dungeon: its enemies disappear and only the return portal is left.")]
    [Min(1f)] public float SurviveSeconds = 60f;
    [Tooltip("Seconds between waves; the first comes when the step starts")]
    [Min(0.5f)] public float WaveInterval = 8f;
    [Tooltip("Each wave is one of these, picked by weight")]
    public List<LevelGeneration.PlacementEnemyGroup> Waves = new List<LevelGeneration.PlacementEnemyGroup>();
    [Tooltip("Meters from the player where waves rise out of the ground; keep it near the screen edge")]
    [Min(1f)] public float WaveDistance = 16f;
    [Tooltip("How far around its point a wave is scattered")]
    [Min(0f)] public float WaveSpread = 3f;
    [Tooltip("No wave comes while this many enemies are alive (all of the level's, not only the waves'). 0: no limit.")]
    [Min(0)] public int MaxAlive = 40;
}

/// <summary>
/// A dungeon: set on a LevelLayoutAuthoring whose Kind is Dungeon. DungeonManager runs its steps; when the last one is
/// done the return portal opens and the finish is saved on the character (by Id).
/// </summary>
[CreateAssetMenu(fileName = "Dungeon", menuName = "Game/Dungeon")]
public class DungeonDefinition : ScriptableObject
{
    [Tooltip("The asset's GUID: what characters' saves remember finished dungeons by")]
    public string Id;
    public string Name;
    [Tooltip("Rolled for every enemy killed in the dungeon; the items land where it died. Use items from LootGenerationManager's tables.")]
    public LootTable KillLootTable;
    [Tooltip("Rolled when the dungeon is finished; the items land in front of the return portal")]
    public LootTable CompletionLootTable;
    [Tooltip("Given to every player when the dungeon is finished")]
    [Min(0)] public int CompletionExperience = 1000;
    [Tooltip("Seconds to finish the dungeon in; running out fails it and no further step starts. The clock stops in " +
             "Survive steps (they have their own). Deaths in Kill steps take time off it. 0: no limit.")]
    [Min(0f)] public float TimeLimit = 300f;
    [Tooltip("Done in order; the last one finishes the dungeon")]
    public List<DungeonStep> Steps = new List<DungeonStep>();
    [Tooltip("Spawned next to the player when the dungeon is finished (the Portal prefab). Leads back where the player came from.")]
    public GameObject ReturnPortal;
    [Tooltip("Leave Dungeon only shows once a character on the account has finished this dungeon: the first dungeon, " +
             "which a new character can't skip")]
    public bool LeaveOnlyOnceFinished;

    [Header("Difficulty")]
    [Tooltip("More enemies for each difficulty above 1, as a share of the level's spawner groups, the Survive waves and their " +
             "Max Alive: 0.15 = +15% per difficulty (difficulty 3: +30%). The enemies themselves grow by their own definitions.")]
    [Min(0f)] public float ExtraEnemiesPerDifficulty = 0.15f;
    [Tooltip("The most the enemy count can be multiplied by, so high difficulties don't slow the game down; past it only the " +
             "enemies grow stronger. 0: no limit.")]
    [Min(0f)] public float MaxEnemyMultiplier = 3f;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? name : Name;

    /// <summary>How many enemies a group of count has at a difficulty (at least one of a group that has any).</summary>
    public int EnemyCount(int count, int difficulty)
    {
        if (count <= 0) return count;
        float multiplier = 1f + ExtraEnemiesPerDifficulty * Mathf.Max(0, difficulty - 1);
        if (MaxEnemyMultiplier > 0f) multiplier = Mathf.Min(multiplier, Mathf.Max(1f, MaxEnemyMultiplier));
        return Mathf.Max(1, Mathf.RoundToInt(count * multiplier));
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        string path = UnityEditor.AssetDatabase.GetAssetPath(this);
        if (string.IsNullOrEmpty(path)) return;
        string guid = UnityEditor.AssetDatabase.AssetPathToGUID(path);
        if (string.IsNullOrEmpty(guid) || guid == Id) return;
        Id = guid;
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
