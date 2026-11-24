using System.Collections.Generic;
using System.Linq;
using Enemies;
using LevelGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds Brightwater Hollow, the first dungeon every new character starts in: its DungeonDefinition (one Kill Boss step,
/// no time limit, no extra enemies per difficulty), the Hero Slime boss, and the level on
/// Scenes/BrightwaterHollow (the biome comes from BiomeTileSets). Set pieces shape it: two rivers across the level (each with a
/// rope bridge in the middle) and the boss arena at the north end, plus ponds and lakes the
/// seed scatters. The solver fills the forest around them. Encounters teach one thing at a time, south to north: a lone
/// Slime, Spitcaps across the first river, Puffshrooms, six mixed encounters (one an ambush), then the boss. Also adds a
/// portal to the dungeon in the Hub and the scene to the build. Safe to run again.
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
    static readonly Vector2Int BossCell = new Vector2Int(8, 54);   // The middle of the boss arena

    // The biome kit's corner connections (pl_wfc_kit CONNECTIONS)
    const int Floor = 0, Trees = 1, Water = 2;

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

    // The boss: a big, tough Slime. Its own skills and passives (leap, spit, poison trail, circling, slimes at 60% and 30%) are set on the asset, not here
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

        // Set pieces (BiomeKitBuilder, from pl_wfc_kit's pieces) shape the level; the solver fills the forest around them.
        // Two rivers across the level, each with a rope bridge in the middle (the second turned around, so it bends the
        // other way); their ends meet the forest at the border
        int river = PieceIndex(layout, "River"), arena = PieceIndex(layout, "BossArena");
        layout.Pieces.Add(new LevelLayoutAuthoring.PlacedPiece { Cell = new Vector2Int(1, 10), Piece = river });
        layout.Pieces.Add(new LevelLayoutAuthoring.PlacedPiece { Cell = new Vector2Int(1, 37), Piece = river, Rotation = 2 });
        // The boss's clearing at the north end: open for the fight, ringed by forest, the way in from the south
        layout.Pieces.Add(new LevelLayoutAuthoring.PlacedPiece { Cell = new Vector2Int(5, 51), Piece = arena });
        // More water each run: ponds and lakes wherever the seed puts them
        layout.Landmarks.Add(new LevelLayoutAuthoring.Landmark { Piece = PieceIndex(layout, "Pond"), MinCount = 1, MaxCount = 3, Spacing = 7f });
        layout.Landmarks.Add(new LevelLayoutAuthoring.Landmark { Piece = PieceIndex(layout, "Lake"), MinCount = 1, MaxCount = 2, Spacing = 9f });

        // The start clearing
        Rect(layout, 6, 1, 12, 6, Floor);

        // Encounters, in the order they're met (trigger radius in meters: they wake as the player gets close), each on a
        // patch of walkable ground so they never stand in a tree wall
        layout.EnemySpawners.Clear();
        layout.Prefabs.Clear();
        Spawner(layout, 9, 7, 16f, 1f, ("Slime", 1));                              // A lone Slime: melee, and it splits
        Spawner(layout, 7, 15, 20f, 1f, ("Spitcap", 1));                           // Spitcaps across the first river: ranged
        Spawner(layout, 11, 15, 20f, 1f, ("Spitcap", 1));
        Spawner(layout, 9, 19, 14f, 3f, ("Puffshroom", 3));                        // Step out of the puddles
        Spawner(layout, 9, 24, 14f, 3f, ("Slime", 1), ("Spitcap", 2));             // Mixed 1: a Slime in front, Spitcaps behind
        Spawner(layout, 9, 29, 14f, 3.5f, ("Puffshroom", 2), ("Slime", 2));        // Mixed 2: Puffshrooms among Slimes
        Spawner(layout, 12, 34, 18f, 2f, ("Spitcap", 2));                          // Mixed 3: Spitcaps off to the side...
        Spawner(layout, 7, 33, 14f, 2.5f, ("Slime", 2));                           // ...and Slimes in the way
        Spawner(layout, 5, 43, 10f, 2f, ("Slime", 2));                             // Mixed 4: an ambush from both sides
        Spawner(layout, 13, 43, 10f, 2f, ("Slime", 1), ("Spitcap", 1));
        Spawner(layout, 9, 46, 14f, 3f, ("Puffshroom", 4), ("Spitcap", 1));        // Mixed 5: Puffshrooms guarding a Spitcap
        Spawner(layout, 9, 49, 14f, 4f, ("Slime", 2), ("Spitcap", 2), ("Puffshroom", 2));   // Mixed 6: everything at once
        foreach (var spawner in layout.EnemySpawners)
            for (int y = spawner.Cell.y; y <= spawner.Cell.y + 1; y++)
                for (int x = spawner.Cell.x; x <= spawner.Cell.x + 1; x++)
                    layout.SetCornerRule(x, y, WfcCornerRule.Walkable);
        layout.Prefabs.Add(new LevelLayoutAuthoring.PlacedPrefab
        {
            Cell = BossCell, Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BossSpawnPrefab), ForceWalkable = true, ReservedRadius = 6f,
        });

        EditorUtility.SetDirty(layout);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    static int PieceIndex(LevelLayoutAuthoring layout, string name)
    {
        int index = layout.TileSet.Pieces.FindIndex(p => p.Name == name);
        if (index < 0) throw new System.InvalidOperationException($"{layout.TileSet.name} has no piece {name}: rebuild the biome's tile set");
        return index;
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
