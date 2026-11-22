using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LevelGeneration;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Builds a biome's WFC tile set from its Blender kit (ArtSource/Tools/pl_wfc_kit.py, exported to
/// Assets/Data/Level/Biomes/&lt;Biome&gt;/Models): kit.json lists the corner and edge connections and every tile (corners, edges,
/// weight, background, mirroring, looks); each model's FBX holds the visual mesh and its colliders, named by suffix. Makes a
/// prefab per model with the biome's materials on its slots (slots.txt) and colliders on the game's layers, then fills the
/// biome's tile set (same asset, so scenes keep it). BiomeTileSets.Build uses it for every biome with a kit.
/// </summary>
public static class BiomeKitBuilder
{
    const string Root = "Assets/Data/Level/Biomes";
    const int WaterLayer = 4;   // Blocks units, not projectiles
    const int GroundLayer = 6;
    const int WallLayer = 7;

    [Serializable] class Kit { public KitCorner[] corners; public KitEdge[] edges; public KitTile[] tiles; }
    [Serializable] class KitCorner { public int id; public string name; public string terrain; public float height; public int higher; }
    [Serializable] class KitEdge { public int id; public string name; public bool walkable; }
    [Serializable] class KitTile { public string name, model; public int[] corners, edges; public float weight; public bool background, mirror, walkThrough; public string[] looks; }

    static string ModelsFolder(BiomeTileSets.Biome biome) => $"{Root}/{biome.Name}/Models";

    public static bool HasKit(BiomeTileSets.Biome biome) => File.Exists($"{ModelsFolder(biome)}/kit.json");

    public static WfcTileSet Build(BiomeTileSets.Biome biome)
    {
        string folder = $"{Root}/{biome.Name}";
        string models = ModelsFolder(biome);
        BiomeTileSets.EnsureFolder(folder + "/KitTiles");
        BiomeTileSets.EnsureFolder(folder + "/Materials");
        AssetDatabase.Refresh();
        ConfigureImports(models);

        var kit = JsonUtility.FromJson<Kit>(File.ReadAllText($"{models}/kit.json"));
        var materials = SlotMaterials(biome, folder);
        var slots = ReadSlots(models);

        // A prefab per model (looks included)
        var prefabs = new Dictionary<string, GameObject>();
        foreach (var name in kit.tiles.SelectMany(t => t.looks.Prepend(t.model)).Distinct())
            prefabs[name] = SavePrefab(name, models, folder + "/KitTiles", slots, materials);

        string setPath = $"{folder}/{biome.Name}TileSet.asset";
        var tileSet = AssetDatabase.LoadAssetAtPath<WfcTileSet>(setPath);
        if (tileSet == null)
        {
            tileSet = ScriptableObject.CreateInstance<WfcTileSet>();
            AssetDatabase.CreateAsset(tileSet, setPath);
        }
        tileSet.CornerConnections = kit.corners.Select(c => new WfcCornerConnection
        {
            Id = c.id, Name = c.name, Height = c.height, Higher = c.higher,
            Terrain = (WfcTerrain)Enum.Parse(typeof(WfcTerrain), c.terrain), Color = ConnectionColor(c),
        }).ToList();
        tileSet.EdgeConnections = kit.edges.Select(e => new WfcEdgeConnection
        {
            Id = e.id, Name = e.name, Walkable = e.walkable, Color = e.walkable ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.8f, 0.35f, 0.25f),
        }).ToList();
        tileSet.Tiles = kit.tiles.Select(t => new WfcTile
        {
            Name = t.name, Prefab = prefabs[t.model], Weight = t.weight, AllowRotations = true, AllowMirror = t.mirror, Background = t.background, WalkThrough = t.walkThrough,
            SouthWest = t.corners[0], SouthEast = t.corners[1], NorthEast = t.corners[2], NorthWest = t.corners[3],
            South = t.edges[0], East = t.edges[1], North = t.edges[2], West = t.edges[3],
            Looks = t.looks.Select(l => prefabs[l]).ToList(),
        }).ToList();
        tileSet.Pieces.Clear();
        tileSet.EnsureConnections();
        EditorUtility.SetDirty(tileSet);
        var backdrop = BuildBackdrop(biome, folder, materials);
        AssetDatabase.SaveAssets();
        Debug.Log($"Biome {biome.Name}: {tileSet.Tiles.Count} kit tiles ({prefabs.Count} models), {tileSet.CornerConnections.Count} corner and " +
                  $"{tileSet.EdgeConnections.Count} edge connections, written to {setPath}" +
                  (backdrop != null ? $"; backdrop with {backdrop.Pieces.Length} pieces." : "."), tileSet);
        return tileSet;
    }

    // object|slot,slot,... per line (pl_wfc_kit / pl_backdrop exports)
    static Dictionary<string, string[]> ReadSlots(string models) =>
        File.ReadAllLines($"{models}/slots.txt").Where(l => !l.StartsWith("#") && l.Contains("|"))
            .ToDictionary(l => l.Split('|')[0], l => l.Split('|')[1].Split(','));

    /// <summary>The biome's backdrop (LevelBackdrop), or null when it has none.</summary>
    public static LevelBackdrop Backdrop(BiomeTileSets.Biome biome) => AssetDatabase.LoadAssetAtPath<LevelBackdrop>($"{Root}/{biome.Name}/{biome.Name}Backdrop.asset");

    /// <summary>
    /// The biome's backdrop from its Blender pieces (Models/Backdrop, ArtSource/Tools/pl_backdrop.py): a prefab per piece,
    /// the mist prefab, and the LevelBackdrop asset (same asset on rebuilds, so tuned values stay; its colors and prefabs
    /// are refreshed). Null when the biome has no pieces.
    /// </summary>
    static LevelBackdrop BuildBackdrop(BiomeTileSets.Biome biome, string folder, Dictionary<string, Material> materials)
    {
        string models = ModelsFolder(biome) + "/Backdrop";
        if (!File.Exists($"{models}/slots.txt")) return null;
        BiomeTileSets.EnsureFolder(folder + "/Backdrop");
        var slots = ReadSlots(models);
        var pieces = slots.Keys.OrderBy(n => n).Select(n => SavePrefab(n, models, folder + "/Backdrop", slots, materials)).ToArray();

        string path = $"{folder}/{biome.Name}Backdrop.asset";
        var backdrop = AssetDatabase.LoadAssetAtPath<LevelBackdrop>(path);
        if (backdrop == null)
        {
            backdrop = ScriptableObject.CreateInstance<LevelBackdrop>();
            AssetDatabase.CreateAsset(backdrop, path);
        }
        // The chasm's fog: the drop, the sunk ring and the sky below the horizon all become this one color
        backdrop.AbyssColor = BiomeTileSets.Hex(biome.ChasmFog ?? biome.Fog);
        backdrop.Mist = SaveMist(biome, folder);
        backdrop.Pieces = pieces;
        EditorUtility.SetDirty(backdrop);
        return backdrop;
    }

    // Sheets of mist (PerfectLoot/Mist) at different depths, each drifting its own way: parallax below the level
    static readonly (float Depth, float Opacity, float Scale, Vector2 Drift, float Lift)[] MistLayers =
    {
        (6f, 0.4f, 0.06f, new Vector2(0.7f, 0.3f), 1.9f),
        (18f, 0.5f, 0.035f, new Vector2(-0.4f, 0.5f), 1.6f),
        (40f, 0.65f, 0.02f, new Vector2(0.25f, -0.2f), 1.35f),
    };
    const float MistSize = 500f;   // Meters per sheet: well past the farthest silhouettes

    static GameObject SaveMist(BiomeTileSets.Biome biome, string folder)
    {
        var shader = Shader.Find("PerfectLoot/Mist");
        var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var root = new GameObject($"{biome.Name}_Mist");
        for (int i = 0; i < MistLayers.Length; i++)
        {
            var layer = MistLayers[i];
            string path = $"{folder}/Materials/{biome.Name}_Mist{i + 1}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetFloat("_Opacity", layer.Opacity);
            material.SetFloat("_Scale", layer.Scale);
            material.SetFloat("_Lift", layer.Lift);
            material.SetVector("_Drift", new Vector4(layer.Drift.x, layer.Drift.y, 0f, 0f));
            material.renderQueue = 2900 + MistLayers.Length - i;   // Deepest first
            EditorUtility.SetDirty(material);

            var sheet = new GameObject($"Mist{i + 1}");
            sheet.transform.SetParent(root.transform, false);
            sheet.transform.localPosition = new Vector3(0f, -layer.Depth, 0f);
            sheet.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // The quad faces up
            sheet.transform.localScale = Vector3.one * MistSize;
            sheet.AddComponent<MeshFilter>().sharedMesh = quad;
            var renderer = sheet.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{folder}/Backdrop/{biome.Name}_Mist.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    // Level geometry reads the meshes at bake time, so they stay readable; materials come from the slots
    static void ConfigureImports(string models)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { models }))
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
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

    static GameObject SavePrefab(string name, string models, string tilesFolder, Dictionary<string, string[]> slots, Dictionary<string, Material> materials)
    {
        string fbx = $"{models}/{name}.fbx";
        var meshes = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Mesh>().ToList();
        var visualMesh = meshes.FirstOrDefault(m => m.name == name) ?? throw new FileNotFoundException($"{fbx} has no mesh named {name}");

        var root = new GameObject(name);
        var visual = new GameObject("Visual");
        visual.transform.SetParent(root.transform, false);
        visual.AddComponent<MeshFilter>().sharedMesh = visualMesh;
        visual.AddComponent<MeshRenderer>().sharedMaterials = slots[name].Select(slot =>
            materials.TryGetValue(slot.Trim(), out var material) ? material : throw new KeyNotFoundException($"No material for slot {slot} of {name}")).ToArray();

        // Colliders by suffix (pl_wfc_kit.colliders): ground and water surfaces can be aimed at
        foreach (var mesh in meshes.Where(m => m != visualMesh))
        {
            string suffix = mesh.name.Substring(name.Length + 1);
            int layer = suffix.StartsWith("ColGround") ? GroundLayer : suffix.StartsWith("ColWater") ? WaterLayer : WallLayer;
            var go = new GameObject(suffix) { layer = layer };
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            if (suffix.StartsWith("ColGround") || suffix.StartsWith("ColWaterSurface")) go.AddComponent<GroundAuthoring>();
        }

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{tilesFolder}/{name}.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    // The biome's materials (BiomeTileSets) for the kit's slots, plus the few the kit adds
    static Dictionary<string, Material> SlotMaterials(BiomeTileSets.Biome b, string folder)
    {
        var parts = BiomeTileSets.BuildMaterials(b, folder);
        string m = folder + "/Materials";
        var shadow = BiomeTileSets.Hex(b.EnvShadow);
        var floor = BiomeTileSets.Hex(b.Floor);
        var result = new Dictionary<string, Material>
        {
            // The kit models its slabs, so the floors are plain colors (a texture would draw a second grid of slabs)
            ["Floor"] = BiomeTileSets.Comic(BiomeTileSets.ComicFloor, $"{m}/{b.Name}_KitFloor.mat", floor, shadow),
            ["FloorAlt"] = BiomeTileSets.Comic(BiomeTileSets.ComicFloor, $"{m}/{b.Name}_KitFloorAlt.mat", BiomeTileSets.Hex(b.FloorAlt), shadow),
            ["Joint"] = BiomeTileSets.Comic(BiomeTileSets.ComicEnv, $"{m}/{b.Name}_Joint.mat", BiomeTileSets.Hex(b.FloorGap), shadow),
            // Paving: the floor's color, lighter
            ["Path"] = BiomeTileSets.Comic(BiomeTileSets.ComicFloor, $"{m}/{b.Name}_Path.mat", floor * 1.3f, shadow),
            // Moss, ash, snow or runes: the floor's color pulled a little toward the accent
            ["Overgrowth"] = BiomeTileSets.Comic(BiomeTileSets.ComicEnv, $"{m}/{b.Name}_Overgrowth.mat",
                                                 Color.Lerp(floor, BiomeTileSets.Hex(b.Accent), 0.22f) * 1.1f, shadow),
            ["Accent"] = parts[BiomeTileGenerator.Part.Accent],
            ["Cliff"] = parts[BiomeTileGenerator.Part.Rock],
            // Rock walls share the cliffs' color: a near-black side made the background rock look like floating plates
            ["WallSide"] = parts[BiomeTileGenerator.Part.Rock],
            ["WallTop"] = parts[BiomeTileGenerator.Part.WallTop],
            ["WallDark"] = parts[BiomeTileGenerator.Part.Bank],
            ["Bank"] = parts[BiomeTileGenerator.Part.Bank],
            ["Chasm"] = parts[BiomeTileGenerator.Part.Chasm],
            ["Liquid"] = parts[BiomeTileGenerator.Part.Liquid],
            ["Glow"] = parts[BiomeTileGenerator.Part.Glow],
            ["Bridge"] = BiomeTileSets.Comic(BiomeTileSets.ComicEnv, $"{m}/{b.Name}_Bridge.mat", BiomeTileSets.Hex("#5E4636"), shadow),
            ["Spire"] = BiomeTileSets.Comic(BiomeTileSets.ComicEnv, $"{m}/{b.Name}_Spire.mat", BiomeTileSets.Hex(b.Rock) * 0.7f, shadow),
        };
        foreach (var material in result.Values) EditorUtility.SetDirty(material);
        return result;
    }

    // Gizmo colors of the level preview, by what the connection is
    static Color ConnectionColor(KitCorner c)
    {
        var color = c.name.Replace("High", "") switch
        {
            "Floor" => new Color(0.45f, 0.75f, 0.35f),
            "Path" => new Color(0.8f, 0.75f, 0.5f),
            "Overgrowth" => new Color(0.3f, 0.6f, 0.5f),
            "Rock" => new Color(0.42f, 0.42f, 0.48f),
            "Drop" => new Color(0.1f, 0.12f, 0.18f),
            "Liquid" => new Color(0.2f, 0.5f, 0.95f),
            "Bridge" => new Color(0.6f, 0.4f, 0.25f),
            _ => Color.magenta,
        };
        return c.height > 0f ? Color.Lerp(color, Color.white, 0.35f) : color;
    }
}
