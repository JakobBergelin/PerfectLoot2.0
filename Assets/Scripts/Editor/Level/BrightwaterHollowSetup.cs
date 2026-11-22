using System.Collections.Generic;
using System.Linq;
using Enemies;
using LevelGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds Brightwater Hollow, the first dungeon every new character starts in: its DungeonDefinition (one Kill Boss step,
/// no time limit, no extra enemies per difficulty), the Hero Slime placeholder boss, and the level painted on
/// Scenes/BrightwaterHollow (the biome comes from BiomeTileSets). The level teaches one thing at a time, south to north:
/// a start clearing, a lone Slime, a river crossed by a rope bridge with Spitcaps on the far bank, a Puffshroom grove,
/// six mixed encounters (one across a lake, one an ambush), a second river and the boss clearing. A creek and the second
/// river run into the lake, so the water flows toward it. Only the path, the water, the clearings and a few tree walls
/// are painted; the solver fills the rest from the seed. Also adds a portal to the dungeon in the Hub and the scene to
/// the build. Safe to run again.
/// </summary>
public static class BrightwaterHollowSetup
{
    const string ScenePath = "Assets/Scenes/BrightwaterHollow.unity";
    const string SubscenePath = "Assets/Scenes/BrightwaterHollow/Entities.unity";
    const string DungeonPath = "Assets/Data/Dungeons/BrightwaterHollow.asset";
    const string BossPath = "Assets/Data/Enemies/HeroSlime.asset";
    const string EnemyFolder = "Assets/Data/Enemies/";
    const string BossSpawnPrefab = "Assets/Prefabs/Dungeon/BossSpawnPoint.prefab";
    const string PortalPrefab = "Assets/Prefabs/Portal.prefab";
    const string EnemyPrefabsPath = "Assets/Prefabs/EnemyPrefabs.prefab";
    const string HubSubscene = "Assets/Scenes/Hub/Entities.unity";
    const string TemplateDungeon = "Assets/Data/Dungeons/TestKillBoss.asset";

    public const int Width = 18, Height = 58;
    static readonly Vector2Int Start = new Vector2Int(9, 3);
    static readonly Vector2Int BossCell = new Vector2Int(9, 56);

    // The biome kit's corner connections (pl_wfc_kit CONNECTIONS)
    const int Floor = 0, Trees = 1, Water = 2, Path = 4, Meadow = 5, Bridge = 6;

    [MenuItem("Tools/Level/Build Brightwater Hollow")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var boss = BuildBoss();
        var dungeon = BuildDungeon(boss);
        AddToEnemyPrefabs(boss);
        AssetDatabase.SaveAssets();
        // Opening scenes unloads unused assets: the scene steps load what they need by path
        PaintLevel(dungeon);
        AddPortalToHub();
        AddToBuild();
        AssetDatabase.SaveAssets();
        Debug.Log($"Brightwater Hollow built: {Width}x{Height} cells, scene {ScenePath}.");
    }

    // ---------- dungeon ----------

    // A placeholder until the real boss is designed: a big, tough Slime that still splits when it dies
    static EnemyDefinition BuildBoss()
    {
        var boss = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(BossPath);
        if (boss == null)
        {
            AssetDatabase.CopyAsset(EnemyFolder + "Slime.asset", BossPath);
            boss = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(BossPath);
        }
        boss.Name = "Hero Slime";
        boss.Scale = 2.4f;
        boss.Mass = 6f;
        boss.CameraFocus = true;
        boss.Stats.Health = 45f;
        boss.Experience = 80;
        boss.MeleeDamage = 1f;
        boss.AttackRadius = 2.6f;
        EditorUtility.SetDirty(boss);
        return boss;
    }

    static DungeonDefinition BuildDungeon(EnemyDefinition boss)
    {
        var dungeon = AssetDatabase.LoadAssetAtPath<DungeonDefinition>(DungeonPath);
        if (dungeon == null)
        {
            var template = AssetDatabase.LoadAssetAtPath<DungeonDefinition>(TemplateDungeon);
            dungeon = ScriptableObject.CreateInstance<DungeonDefinition>();
            dungeon.Id = System.Guid.NewGuid().ToString("N");
            dungeon.KillLootTable = template.KillLootTable;
            dungeon.CompletionLootTable = template.CompletionLootTable;
            dungeon.ReturnPortal = template.ReturnPortal;
            AssetDatabase.CreateAsset(dungeon, DungeonPath);
        }
        dungeon.Name = "Brightwater Hollow";
        dungeon.CompletionExperience = 500;
        dungeon.TimeLimit = 0f;                       // A first dungeon: take your time
        dungeon.ExtraEnemiesPerDifficulty = 0f;       // Learning, not a swarm
        dungeon.MaxEnemyMultiplier = 1f;
        dungeon.LeaveOnlyOnceFinished = true;         // A new character can't skip it
        dungeon.Steps = new List<DungeonStep> { new DungeonStep { Kind = DungeonStepKind.KillBoss, Boss = boss, BossTriggerRadius = 22f } };
        EditorUtility.SetDirty(dungeon);
        return dungeon;
    }

    static void AddToEnemyPrefabs(EnemyDefinition boss)
    {
        var root = PrefabUtility.LoadPrefabContents(EnemyPrefabsPath);
        var list = root.GetComponent<EnemyPrefabsAuthoring>();
        if (!list.Definitions.Contains(boss))
        {
            list.Definitions.Add(boss);
            PrefabUtility.SaveAsPrefabAsset(root, EnemyPrefabsPath);
        }
        PrefabUtility.UnloadPrefabContents(root);
    }

    // ---------- the level ----------

    static void PaintLevel(DungeonDefinition dungeon)
    {
        var scene = EditorSceneManager.OpenScene(SubscenePath, OpenSceneMode.Single);
        var layout = scene.GetRootGameObjects().Select(r => r.GetComponentInChildren<LevelLayoutAuthoring>(true)).First(l => l != null);
        layout.Kind = AreaKind.Dungeon;
        layout.Dungeon = dungeon;
        layout.Width = Width;
        layout.Height = Height;
        layout.PlayerStart = Start;
        layout.Border = WfcCornerRule.NotWalkable;
        layout.Mode = LevelGenerationMode.GenerateAtRuntime;
        layout.RandomSeedEachPlay = true;
        layout.HillHeight = 0.3f;
        layout.Placements = null;
        layout.ForcedTiles.Clear();
        layout.Pieces.Clear();
        layout.Landmarks.Clear();
        layout.ClearCornerRules();

        // Clearings: the start and the boss's
        Rect(layout, 6, 1, 12, 6, Floor);
        Rect(layout, 5, 54, 13, 57, Floor);

        // The path north, all the way to the boss clearing
        Rect(layout, 9, 4, 9, 53, Path);

        // First river with its bridge, a creek from it along the east side into the lake, and the second river (fed by
        // the lake's north shore) with its bridge
        Rect(layout, 1, 12, 17, 13, Water);
        Rect(layout, 17, 14, 17, 29, Water);       // Against the border: no strip of land cut off behind it
        Rect(layout, 11, 30, 17, 36, Water);
        Rect(layout, 15, 37, 17, 39, Water);       // Lake into the second river, out to the border
        Rect(layout, 1, 40, 17, 41, Water);
        foreach (int y in new[] { 12, 13, 40, 41 }) layout.SetCornerRule(9, y, WfcCornerRule.Walkable, Bridge);
        foreach (int y in new[] { 11, 14, 39, 42 }) Rect(layout, 8, y, 10, y, Path);     // Bridge ends: three corners wide
        // Grass between the path and the water along the lake, so the path stays a path; grass where the Spitcaps stand
        // on the first river's far bank and on the lake's north shore
        Rect(layout, 10, 29, 10, 37, Floor);
        Rect(layout, 5, 14, 7, 16, Floor);
        Rect(layout, 11, 14, 13, 16, Floor);
        Rect(layout, 11, 37, 14, 39, Floor);

        // The Puffshroom grove: a meadow west of the path, tree walls narrowing the way after the first river
        Rect(layout, 3, 17, 6, 20, Meadow);
        Rect(layout, 1, 22, 4, 26, Trees);
        Rect(layout, 13, 22, 14, 26, Trees);
        // The ambush clearing past the second river: open grass on both sides of the path
        Rect(layout, 3, 43, 15, 46, Floor);
        Rect(layout, 9, 43, 9, 46, Path);

        // Encounters, in the order they're met (trigger radius in meters: they wake as the player gets close)
        layout.EnemySpawners.Clear();
        layout.Prefabs.Clear();
        Spawner(layout, 9, 8, 16f, 1f, ("Slime", 1));                              // A lone Slime: melee, and it splits
        Spawner(layout, 6, 15, 20f, 1f, ("Spitcap", 1));                           // Spitcaps across the river: ranged
        Spawner(layout, 12, 15, 20f, 1f, ("Spitcap", 1));
        Spawner(layout, 9, 19, 14f, 3f, ("Puffshroom", 3));                        // The grove: step out of the puddles
        Spawner(layout, 9, 24, 14f, 3f, ("Slime", 1), ("Spitcap", 2));             // Mixed 1: a Slime in front, Spitcaps behind
        Spawner(layout, 9, 28, 14f, 3.5f, ("Puffshroom", 2), ("Slime", 2));        // Mixed 2: Puffshrooms among Slimes
        Spawner(layout, 12, 38, 18f, 2f, ("Spitcap", 2));                          // Mixed 3: Spitcaps on the lake's far shore...
        Spawner(layout, 8, 34, 14f, 2.5f, ("Slime", 2));                           // ...and Slimes on the path beside it
        Spawner(layout, 5, 45, 10f, 2f, ("Slime", 2));                             // Mixed 4: an ambush from both sides
        Spawner(layout, 13, 45, 10f, 2f, ("Slime", 1), ("Spitcap", 1));
        Spawner(layout, 9, 49, 14f, 3f, ("Puffshroom", 4), ("Spitcap", 1));        // Mixed 5: Puffshrooms guarding a Spitcap
        Spawner(layout, 9, 52, 14f, 4f, ("Slime", 2), ("Spitcap", 2), ("Puffshroom", 2));   // Mixed 6: everything at once
        layout.Prefabs.Add(new LevelLayoutAuthoring.PlacedPrefab
        {
            Cell = BossCell, Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BossSpawnPrefab), ForceWalkable = true, ReservedRadius = 6f,
        });

        EditorUtility.SetDirty(layout);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    // Corners x0..x1, y0..y1 (inclusive) painted with one connection, as the terrain it plays as
    static void Rect(LevelLayoutAuthoring layout, int x0, int y0, int x1, int y1, int connection)
    {
        var rule = connection == Trees ? WfcCornerRule.NotWalkable : connection == Water ? WfcCornerRule.Water : WfcCornerRule.Walkable;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                layout.SetCornerRule(x, y, rule, connection);
    }

    static void Spawner(LevelLayoutAuthoring layout, int x, int y, float triggerRadius, float spawnRadius, params (string Enemy, int Count)[] group)
    {
        layout.EnemySpawners.Add(new LevelLayoutAuthoring.SpawnerMarker
        {
            Cell = new Vector2Int(x, y), TriggerRadius = triggerRadius, SpawnRadius = spawnRadius,
            Group = group.Select(g => new EnemySpawner.Entry
            {
                Definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>($"{EnemyFolder}{g.Enemy}.asset"), Count = g.Count,
            }).ToList(),
        });
    }

    // ---------- the way there ----------

    // A portal to the dungeon in the Hub, beside the others, so it can be played again from town
    static void AddPortalToHub()
    {
        var scene = EditorSceneManager.OpenScene(HubSubscene, OpenSceneMode.Single);
        if (scene.GetRootGameObjects().Any(r => r.name == "Portal to BrightwaterHollow")) return;
        var portal = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PortalPrefab), scene);
        portal.name = "Portal to BrightwaterHollow";
        portal.transform.position = new Vector3(34f, 0f, 14f);
        var authoring = portal.GetComponent<PortalAuthoring>();
        authoring.Scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        authoring.DisplayName = "Brightwater Hollow";
        PrefabUtility.RecordPrefabInstancePropertyModifications(authoring);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    static void AddToBuild()
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == ScenePath)) return;
        scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
