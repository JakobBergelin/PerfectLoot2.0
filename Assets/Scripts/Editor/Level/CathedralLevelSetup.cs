using System.Collections.Generic;
using System.IO;
using System.Linq;
using Enemies;
using LevelGeneration;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the Haunted Cathedral level from ArtSource/Cathedral_Level.blend's tile exports (pl_wfc_tiles.py) and the
/// animated cathedral models: tile and prop prefabs, the WFC tile set (cracks use the Water slot), rigid visual prefabs
/// for the player and the three enemies, their EnemyDefinitions, and Scenes/HauntedCathedral (a copy of SampleScene with
/// cathedral lighting, the tile set and enemy spawners). Safe to run again; existing assets keep their GUIDs and the
/// scene's layout rules are only set when the scene is first made.
/// </summary>
public static class CathedralLevelSetup
{
    const string Root = "Assets/Data/Level/Cathedral";
    const string ModelsFolder = Root + "/Models";
    const string TilesFolder = Root + "/Tiles";
    const string PropsFolder = Root + "/Props";
    const string TileSetPath = Root + "/CathedralTileSet.asset";
    const string MaterialsFolder = "Assets/ArtStyleTest/Materials/ComicCathedral";
    const string ArtPrefabs = "Assets/ArtStyleTest/Prefabs/ComicCathedral";
    const string VisualsFolder = "Assets/Prefabs/Cathedral";
    const string EnemiesFolder = "Assets/Data/Enemies/Cathedral";
    const string ScenePath = "Assets/Scenes/HauntedCathedral.unity";
    const string SubscenePath = "Assets/Scenes/HauntedCathedral/Entities.unity";

    const int WaterLayer = 4;   // Blocks units, not projectiles
    const int GroundLayer = 6;
    const int WallLayer = 7;
    const float TileSize = 5f;

    [MenuItem("Tools/Level/Build Haunted Cathedral")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        AssetDatabase.Refresh();
        EnsureFolder(TilesFolder);
        EnsureFolder(PropsFolder);
        EnsureFolder(VisualsFolder);
        EnsureFolder(EnemiesFolder);

        var materials = LoadMaterials();
        ConfigureTileImports();
        var props = BuildProps();
        var tileSet = BuildTileSet(materials, props);
        var visuals = BuildVisuals();
        var definitions = BuildDefinitions(visuals);
        int tiles = tileSet.Tiles.Count;
        // Opening a scene unloads unused assets: save everything and hand the scene step paths
        AssetDatabase.SaveAssets();
        BuildScene(AssetDatabase.GetAssetPath(visuals["PL_Player"]), definitions.Select(AssetDatabase.GetAssetPath).ToList());
        AssetDatabase.SaveAssets();
        Debug.Log($"Haunted Cathedral built: {tiles} tiles, {definitions.Count} enemies, scene {ScenePath}.");
    }

    // ---------- materials ----------

    static Dictionary<string, Material> LoadMaterials()
    {
        // Faint ruby glow deep in the cracks: the stained glass's ruby, dark and soft (it shouldn't read as a pool)
        string glowPath = $"{MaterialsFolder}/CT_ChasmGlow.mat";
        var glow = AssetDatabase.LoadAssetAtPath<Material>(glowPath);
        if (glow == null)
        {
            glow = new Material(AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/CT_GlassRed.mat"));
            AssetDatabase.CreateAsset(glow, glowPath);
        }
        glow.SetColor("_BaseColor", Hex("#3A1622"));
        glow.SetColor("_EmissionColor", new Color(1f, 0.069f, 0.158f) * 0.35f);
        glow.SetFloat("_EmissionPulse", 0.3f);
        glow.SetFloat("_EmissionPulseSpeed", 0.8f);
        glow.SetFloat("_OutlineWidth", 0f);
        EditorUtility.SetDirty(glow);
        return AssetDatabase.FindAssets("t:Material", new[] { MaterialsFolder })
            .Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g)))
            .ToDictionary(m => m.name);
    }

    static Material[] SlotMaterials(string slotsPath, string objectName, Dictionary<string, Material> materials, string prefix = "")
    {
        var line = File.ReadAllLines(slotsPath).First(l => l.StartsWith(objectName + "|"));
        return line.Split('|')[1].Split(',').Select(n =>
        {
            if (!materials.TryGetValue(prefix + n.Trim(), out var m)) throw new FileNotFoundException($"Material {prefix}{n} for {objectName}");
            return m;
        }).ToArray();
    }

    // ---------- tiles ----------

    static void ConfigureTileImports()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ModelsFolder }))
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            // Level geometry reads the meshes at bake time, so they must stay readable
            if (importer.isReadable && importer.materialImportMode == ModelImporterMaterialImportMode.None && !importer.importAnimation) continue;
            importer.isReadable = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.SaveAndReimport();
        }
    }

    struct PropPlacement
    {
        public string Prop;
        public Vector3 Position;
        public float Yaw;
        public PropPlacement(string prop, float x, float z, float yaw) { Prop = prop; Position = new Vector3(x, 0f, z); Yaw = yaw; }
    }

    static WfcTileSet BuildTileSet(Dictionary<string, Material> materials, Dictionary<string, GameObject> props)
    {
        var tileSet = AssetDatabase.LoadAssetAtPath<WfcTileSet>(TileSetPath);
        if (tileSet == null)
        {
            tileSet = ScriptableObject.CreateInstance<WfcTileSet>();
            AssetDatabase.CreateAsset(tileSet, TileSetPath);
        }
        tileSet.Tiles.Clear();
        tileSet.Pieces.Clear();
        string slots = $"{ModelsFolder}/slots.txt";

        void AddTile(string name, string model, string corners, float weight, params PropPlacement[] placements)
        {
            var prefab = SaveTilePrefab(name, model, slots, materials, props, placements);
            var c = corners.Select(Terrain).ToArray();
            tileSet.Tiles.Add(new WfcTile
            {
                Name = name,
                Prefab = prefab,
                SouthWest = c[0], SouthEast = c[1], NorthEast = c[2], NorthWest = c[3],
                Weight = weight,
                AllowRotations = true,
            });
        }

        foreach (var path in Directory.GetFiles(ModelsFolder, "Tile_????.fbx").Select(p => p.Replace('\\', '/')).OrderBy(p => p))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            string corners = name.Substring(5, 4);
            AddTile(name, name, corners, corners == "FFFF" ? FloorShare(10f) : TileWeight(corners));
        }

        // Plain floor variety (other slab patterns) and floor with furniture (shares of 30 with Tile_FFFF's 10)
        AddTile("Tile_FFFF_2", "Tile_FFFF_2", "FFFF", FloorShare(6f));
        AddTile("Tile_FFFF_3", "Tile_FFFF_3", "FFFF", FloorShare(6f));
        AddTile("Tile_FFFF_Pews", "Tile_FFFF", "FFFF", FloorShare(2f), new PropPlacement("Pews", 0f, 0.9f, 0f), new PropPlacement("Pews", 0f, -1.1f, 0f));
        AddTile("Tile_FFFF_Tomb", "Tile_FFFF_2", "FFFF", FloorShare(1.4f), new PropPlacement("Tombs_1", 0.3f, 0.2f, 90f), new PropPlacement("Rubble", -1.6f, -1.4f, 35f));
        AddTile("Tile_FFFF_Tombs", "Tile_FFFF_3", "FFFF", FloorShare(1.2f), new PropPlacement("Tombs_2", -0.9f, 0.4f, 0f), new PropPlacement("Tombs_1", 1.2f, -0.6f, 0f));
        AddTile("Tile_FFFF_Candles", "Tile_FFFF", "FFFF", FloorShare(1.6f), new PropPlacement("Candelabra", -1.5f, 1.4f, 0f), new PropPlacement("Candelabra", 1.5f, -1.4f, 180f));
        AddTile("Tile_FFFF_Rubble", "Tile_FFFF_2", "FFFF", FloorShare(1.8f), new PropPlacement("Rubble", 0.8f, -0.6f, 30f), new PropPlacement("Rubble", -1.3f, 1.1f, 200f));
        // Along a wall (wall in the south half, floor north): candles against it
        AddTile("Tile_WWFF_Candles", "Tile_WWFF", "WWFF", TileWeight("WWFF") * 0.3f, new PropPlacement("Candelabra", -1.4f, 1.1f, 0f), new PropPlacement("Candelabra", 1.4f, 1.1f, 0f));

        var arena = new WfcPiece { Name = "Arena 2x2", Size = new Vector2Int(2, 2), AllowRotations = true };
        arena.EnsureCorners();
        float p = 4.2f;
        arena.Prefab = SaveTilePrefab("Arena_2x2", "Arena_2x2", slots, materials, props, new[]
        {
            new PropPlacement("Pillar", -p, -p, 0f), new PropPlacement("Pillar", p, -p, 90f),
            new PropPlacement("Pillar", p, p, 180f), new PropPlacement("Pillar", -p, p, 270f),
            new PropPlacement("Candelabra", 0f, 4f, 0f), new PropPlacement("Candelabra", 0f, -4f, 180f),
        });
        tileSet.Pieces.Add(arena);

        EditorUtility.SetDirty(tileSet);
        return tileSet;
    }

    static WfcTerrain Terrain(char letter) => letter switch
    {
        'F' => WfcTerrain.Walkable,
        'W' => WfcTerrain.Wall,
        _ => WfcTerrain.Water,
    };

    // Open naves: mostly floor, walls as broken runs rather than a maze, the odd crack. The solver grows the level from
    // the walled border inwards, where only transition tiles fit, so what decides the mix is how much each tile favours
    // floor corners: a tile weighs the product of its corners' factors (about 2/3 floor, 1/4 wall, 1/12 crack).
    static float TileWeight(string corners)
    {
        float weight = 1f;
        foreach (char c in corners) weight *= c == 'F' ? 4f : c == 'W' ? 1f : 0.45f;
        return weight;
    }

    // Plain floor tiles share the FFFF weight
    static float FloorShare(float share) => TileWeight("FFFF") * share / 30f;

    static GameObject SaveTilePrefab(string name, string model, string slots, Dictionary<string, Material> materials,
                                     Dictionary<string, GameObject> props, IEnumerable<PropPlacement> placements)
    {
        string fbx = $"{ModelsFolder}/{model}.fbx";
        var meshes = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Mesh>().ToDictionary(m => m.name);
        if (!meshes.TryGetValue(model, out var visualMesh)) throw new FileNotFoundException($"{fbx} has no mesh named {model}");

        var root = new GameObject(name);
        var visual = new GameObject("Visual");
        visual.transform.SetParent(root.transform, false);
        visual.AddComponent<MeshFilter>().sharedMesh = visualMesh;
        visual.AddComponent<MeshRenderer>().sharedMaterials = SlotMaterials(slots, model, materials);

        void Collider(string suffix, int layer, bool ground)
        {
            if (!meshes.TryGetValue($"{model}_{suffix}", out var mesh)) return;
            var go = new GameObject(suffix) { layer = layer };
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            if (ground) go.AddComponent<GroundAuthoring>();
        }
        Collider("ColGround", GroundLayer, true);
        Collider("ColWall", WallLayer, false);
        Collider("ColCrackSurface", WaterLayer, true);
        Collider("ColCrackBlocker", WaterLayer, false);

        foreach (var placement in placements)
        {
            var prop = props[placement.Prop];
            var marker = new GameObject("Prop_" + placement.Prop);
            marker.transform.SetParent(root.transform, false);
            marker.transform.localPosition = placement.Position;
            marker.transform.localRotation = Quaternion.Euler(0f, placement.Yaw, 0f);
            marker.AddComponent<WfcPropAuthoring>().Prefab = prop;
            // Preview only: everything under a prop marker is left out of the tile geometry
            var preview = (GameObject)PrefabUtility.InstantiatePrefab(prop);
            preview.transform.SetParent(marker.transform, false);
        }

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{TilesFolder}/{name}.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    // ---------- props ----------

    // The art test's cathedral models with a collider (on Wall: blocks units and projectiles); rubble is walked over
    static Dictionary<string, GameObject> BuildProps()
    {
        var props = new Dictionary<string, GameObject>();
        foreach (var (name, blocks, shrink) in new[]
                 {
                     ("Pews", true, 0.9f), ("Tombs_1", true, 0.95f), ("Tombs_2", true, 0.95f),
                     ("Candelabra", true, 0.5f), ("Pillar", true, 0.85f), ("Rubble", false, 1f),
                 })
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ArtPrefabs}/CT_ENV_{name}.prefab");
            if (model == null) throw new FileNotFoundException($"{ArtPrefabs}/CT_ENV_{name}.prefab");
            var root = new GameObject("CT_Prop_" + name) { layer = blocks ? WallLayer : 0 };
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.transform.SetParent(root.transform, false);
            instance.transform.localPosition = Vector3.zero;
            if (blocks)
            {
                var bounds = new Bounds(Vector3.zero, Vector3.zero);
                bool first = true;
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
                {
                    if (first) { bounds = renderer.bounds; first = false; }
                    else bounds.Encapsulate(renderer.bounds);
                }
                var box = root.AddComponent<BoxCollider>();
                box.center = new Vector3(bounds.center.x, bounds.size.y * 0.5f, bounds.center.z);
                box.size = new Vector3(bounds.size.x * shrink, bounds.size.y, bounds.size.z * shrink);
            }
            props[name] = PrefabUtility.SaveAsPrefabAsset(root, $"{PropsFolder}/CT_Prop_{name}.prefab");
            Object.DestroyImmediate(root);
        }
        return props;
    }

    // ---------- units ----------

    static Dictionary<string, GameObject> BuildVisuals()
    {
        var enemyTemplate = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/EnemyVisuals.prefab");
        var playerTemplate = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerVisuals.prefab");
        var visuals = new Dictionary<string, GameObject>();

        Material[] Materials(string animatedPrefab, bool elite)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(animatedPrefab).GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterials;
            if (!elite) return source;
            return source.Select(m => AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/Elite_{m.name}.mat") ?? m).ToArray();
        }

        void Add(string key, string fbx, string materialsFrom, bool elite, GameObject template)
        {
            visuals[key] = RigidModelBuilder.Build(new RigidModelBuilder.Settings
            {
                Fbx = fbx,
                Materials = Materials(materialsFrom, elite),
                Template = template,
                Folder = VisualsFolder,
                Name = key + "_Visuals",
            });
        }

        const string enemyModels = "Assets/ArtStyleTest/Models/ComicCathedral/Animated";
        Add("CT_Revenant", $"{enemyModels}/CT_Revenant_Animated.fbx", $"{ArtPrefabs}/CT_Revenant_Animated.prefab", false, enemyTemplate);
        Add("CT_Revenant_Elite", $"{enemyModels}/CT_Revenant_Animated.fbx", $"{ArtPrefabs}/CT_Revenant_Animated.prefab", true, enemyTemplate);
        Add("CT_Wraith", $"{enemyModels}/CT_Wraith_Animated.fbx", $"{ArtPrefabs}/CT_Wraith_Animated.prefab", false, enemyTemplate);
        Add("CT_BellRinger", $"{enemyModels}/CT_BellRinger_Animated.fbx", $"{ArtPrefabs}/CT_BellRinger_Animated.prefab", false, enemyTemplate);
        Add("PL_Player", "Assets/ArtStyleTest/Models/Comic/Animated/PL_Player_Animated.fbx", "Assets/ArtStyleTest/Prefabs/Comic/PL_Player_Animated.prefab", false, playerTemplate);
        return visuals;
    }

    static List<EnemyDefinition> BuildDefinitions(Dictionary<string, GameObject> visuals)
    {
        var list = new List<EnemyDefinition>();
        // Stats only when the definition is first made, so tuning in the inspector survives a rebuild
        EnemyDefinition Define(string asset, string displayName, string visual, System.Action<EnemyDefinition> stats)
        {
            string path = $"{EnemiesFolder}/{asset}.asset";
            var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<EnemyDefinition>();
                definition.Name = displayName;
                stats(definition);
                AssetDatabase.CreateAsset(definition, path);
            }
            definition.Prefab = visuals[visual];
            definition.OverrideColor = false;
            definition.BodyType = BodyType.Physics;
            EditorUtility.SetDirty(definition);
            list.Add(definition);
            return definition;
        }

        Define("Revenant", "Armored Revenant", "CT_Revenant", d =>
        {
            d.Mass = 3f; d.Stats.Health = 40f; d.Stats.MovementSpeed = 1.6f; d.Stats.Armor = 5;
            d.MeleeDamage = 1.6f; d.SecondsBetweenAttacks = 1.6f; d.TurnSpeed = 1.6f;
        });
        Define("RevenantElite", "Armored Revenant (Elite)", "CT_Revenant_Elite", d =>
        {
            d.Scale = 1.25f; d.Mass = 5f; d.Stats.Health = 80f; d.Stats.MovementSpeed = 1.6f; d.Stats.Armor = 10;
            d.MeleeDamage = 2.4f; d.SecondsBetweenAttacks = 1.6f; d.TurnSpeed = 1.6f; d.AttackRadius = 3.5f;
        });
        Define("Wraith", "Wraith", "CT_Wraith", d =>
        {
            d.Mass = 0.8f; d.Stats.Health = 15f; d.Stats.MovementSpeed = 3.5f;
            d.MeleeDamage = 0.8f; d.SecondsBetweenAttacks = 1f; d.TurnSpeed = 4f; d.TurnAcceleration = 2f; d.DetectionRadius = 18f;
        });
        Define("BellRinger", "Bell-Ringer", "CT_BellRinger", d =>
        {
            d.Mass = 1.5f; d.Stats.Health = 25f; d.Stats.MovementSpeed = 2.2f;
            d.MeleeDamage = 1f; d.SecondsBetweenAttacks = 1.5f;
        });
        return list;
    }

    // ---------- scene ----------

    static void BuildScene(string playerVisualsPath, List<string> definitionPaths)
    {
        bool created = !File.Exists(ScenePath);
        if (created)
        {
            EnsureFolder(Path.GetDirectoryName(SubscenePath).Replace('\\', '/'));
            AssetDatabase.CopyAsset("Assets/Scenes/SampleScene.unity", ScenePath);
            AssetDatabase.CopyAsset("Assets/Scenes/SampleScene/Entities.unity", SubscenePath);
        }

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SceneManager.SetActiveScene(scene);
        foreach (var root in scene.GetRootGameObjects())
        {
            var subScene = root.GetComponent<SubScene>();
            if (subScene != null) subScene.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(SubscenePath);
            var light = root.GetComponent<Light>();
            if (light != null && light.type == LightType.Directional)
            {
                light.color = Hex("#B4C2F2");
                light.intensity = 1.15f;
                light.shadowStrength = 0.85f;
            }
            var camera = root.GetComponent<Camera>();
            if (camera != null)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Hex("#1E1A2C");
            }
        }
        // Moonlit nave: cool purple ambient and fog
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Hex("#4C4A6E");
        RenderSettings.ambientEquatorColor = Hex("#2E2C42");
        RenderSettings.ambientGroundColor = Hex("#16141C");
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = Hex("#1E1A2C");
        RenderSettings.fogStartDistance = 30f;
        RenderSettings.fogEndDistance = 75f;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        var subscene = EditorSceneManager.OpenScene(SubscenePath, OpenSceneMode.Additive);
        var tileSet = AssetDatabase.LoadAssetAtPath<WfcTileSet>(TileSetPath);
        var playerVisuals = AssetDatabase.LoadAssetAtPath<GameObject>(playerVisualsPath);
        var definitions = definitionPaths.Select(AssetDatabase.LoadAssetAtPath<EnemyDefinition>).ToList();
        foreach (var root in subscene.GetRootGameObjects())
        {
            // The sample's flat ground plane would show through the cracks
            if (root.name == "Ground" && root.GetComponent<GroundAuthoring>() != null) { Object.DestroyImmediate(root); continue; }

            // Some of these are prefab instances: record the changes as overrides, or they aren't saved
            var player = root.GetComponent<PlayerPrefabAuthoring>();
            if (player != null)
            {
                player.PlayerVisualPrefab = playerVisuals;
                PrefabUtility.RecordPrefabInstancePropertyModifications(player);
            }
            var enemies = root.GetComponent<EnemyPrefabsAuthoring>();
            if (enemies != null)
            {
                enemies.Definitions = new List<EnemyDefinition>(definitions);
                PrefabUtility.RecordPrefabInstancePropertyModifications(enemies);
            }
            var layout = root.GetComponent<LevelLayoutAuthoring>();
            if (layout != null)
            {
                ConfigureLayout(layout, tileSet, definitions, created);
                PrefabUtility.RecordPrefabInstancePropertyModifications(layout);
            }
        }
        EditorSceneManager.MarkSceneDirty(subscene);
        bool saved = EditorSceneManager.SaveScene(subscene);
        Debug.Log($"Cathedral subscene {subscene.path}: {subscene.rootCount} roots, new scene {created}, saved {saved}, player {(playerVisuals != null ? playerVisuals.name : "none")}, {definitions.Count(d => d != null)} enemies.");
        EditorSceneManager.CloseScene(subscene, true);
    }

    static void ConfigureLayout(LevelLayoutAuthoring layout, WfcTileSet tileSet, List<EnemyDefinition> definitions, bool created)
    {
        layout.TileSet = tileSet;
        layout.TileSize = TileSize;
        // Rules and spawners only until the layout has cathedral spawners, so hand-painted changes survive a rebuild
        bool configured = layout.EnemySpawners.Any(s => s != null && s.Group.Any(g => g.Definition != null && definitions.Contains(g.Definition)));
        if (configured && !created) return;

        layout.Width = 24;
        layout.Height = 24;
        layout.Border = WfcCornerRule.Wall;
        layout.Mode = LevelGenerationMode.GenerateAtRuntime;
        layout.RandomSeedEachPlay = true;
        // A flat-floored nave: only a little wobble and no big bend or hills
        layout.WobbleStrength = 0.06f;
        layout.BendAngle = 0f;
        layout.HillHeight = 0.4f;
        layout.transform.position = new Vector3(-layout.Width * TileSize * 0.5f, 0f, -layout.Height * TileSize * 0.5f);

        layout.ClearCornerRules();
        layout.EnsureCornerRules();
        layout.ForcedTiles.Clear();
        // The player starts in the south, on the crossing, and fights north
        layout.Pieces = new List<LevelLayoutAuthoring.PlacedPiece> { new LevelLayoutAuthoring.PlacedPiece { Cell = new Vector2Int(11, 3), Piece = 0 } };
        layout.PlayerStart = new Vector2Int(11, 3);

        var byName = definitions.ToDictionary(d => d.name);
        LevelLayoutAuthoring.SpawnerMarker Spawner(int x, int y, params (string Enemy, int Count)[] group)
        {
            // Spawners stand on floor: their cell's corners are always Walkable
            for (int dy = 0; dy <= 1; dy++)
                for (int dx = 0; dx <= 1; dx++)
                    layout.SetCornerRule(x + dx, y + dy, WfcCornerRule.Walkable);
            return new LevelLayoutAuthoring.SpawnerMarker
            {
                Cell = new Vector2Int(x, y),
                Group = group.Select(g => new EnemySpawner.Entry { Definition = byName[g.Enemy], Count = g.Count }).ToList(),
            };
        }
        layout.EnemySpawners = new List<LevelLayoutAuthoring.SpawnerMarker>
        {
            Spawner(6, 8, ("Wraith", 4)),
            Spawner(17, 9, ("Revenant", 2), ("BellRinger", 1)),
            Spawner(11, 13, ("Wraith", 3), ("Revenant", 1)),
            Spawner(5, 17, ("BellRinger", 1), ("Wraith", 3)),
            Spawner(18, 17, ("Revenant", 3)),
            Spawner(12, 21, ("RevenantElite", 1), ("Wraith", 2), ("BellRinger", 1)),
        };
        EditorUtility.SetDirty(layout);
    }

    // ---------- helpers ----------

    static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var color) ? color : Color.magenta;

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
        AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
    }
}
