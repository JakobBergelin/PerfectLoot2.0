using System.Collections.Generic;
using System.IO;
using System.Linq;
using Equipment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the equipable player from ArtSource/Player_Equipment.blend's exports (pl_player_equipment.py): the base body's
/// rigid visual prefab with equipment sockets, a part prefab per item and socket, an EquipmentVisual per item, the
/// registry and player prefab in the Haunted Cathedral subscene, and the visual bases of the uniques.
/// Safe to run again; existing assets keep their GUIDs.
/// </summary>
public static class EquipmentSetup
{
    const string BaseFbx = "Assets/ArtStyleTest/Models/Comic/Animated/PL_Base_Animated.fbx";
    const string BaseSlots = "Assets/ArtStyleTest/Models/Comic/Animated/slots.txt";
    const string ItemModels = "Assets/ArtStyleTest/Models/Equipment";
    const string PlayerFolder = "Assets/Prefabs/Player";
    const string PartsFolder = "Assets/Data/Equipment/Parts";
    const string VisualsFolder = "Assets/Data/Equipment/Visuals";
    const string ItemsFolder = "Assets/Data/Items";
    const string SubscenePath = "Assets/Scenes/HauntedCathedral/Entities.unity";
    static readonly string[] MaterialFolders = { "Assets/ArtStyleTest/Materials/Comic", "Assets/ArtStyleTest/Materials/ComicCathedral" };
    static readonly string[] Clips = { "Idle", "Move", "Hit", "Cast", "Release", "Shoot", "PunchL", "PunchR", "Spin", "Dash", "Sweep" };
    static readonly string[] LoopingClips = { "Idle", "Move", "Cast", "Spin", "Dash" };
    static readonly string[] Sockets = { "hips", "leg_L", "leg_R", "chest", "head", "arm_L", "arm_R", "hand_L", "hand_R" };

    // Which base item each unique or legendary looks like on the character
    static readonly Dictionary<string, string> UniqueBases = new Dictionary<string, string>
    {
        ["MasterSword"] = "ShortSword", ["BrokenPromises"] = "Dagger", ["Vanguard"] = "TowerShield",
        ["CrownOfAges"] = "Cap", ["HeartOfTheWyrm"] = "FullPlateMail", ["LegsOfTheFallen"] = "MailChausses",
        ["Stormstriders"] = "LeatherBoots", ["FistsOfRuin"] = "Gauntlets", ["MantleOfTheWatcher"] = "ClothMantle",
        ["GirdleOfGiants"] = "PlatedBelt", ["EyeOfTheStorm"] = "JadeAmulet", ["SignetOfTheSage"] = "GoldRing",
    };

    [MenuItem("Tools/Equipment/Build Player Equipment")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        AssetDatabase.Refresh();
        foreach (var folder in new[] { PlayerFolder, PartsFolder, VisualsFolder }) EnsureFolder(folder);

        var materials = MaterialFolders.SelectMany(f => AssetDatabase.FindAssets("t:Material", new[] { f }))
            .Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g)))
            .GroupBy(m => m.name).ToDictionary(g => g.Key, g => g.First());
        var items = AssetDatabase.FindAssets("t:RollableItem", new[] { ItemsFolder })
            .Select(g => AssetDatabase.LoadAssetAtPath<RollableItem>(AssetDatabase.GUIDToAssetPath(g)))
            .ToDictionary(i => i.name);

        ImportBase();
        var player = RigidModelBuilder.Build(new RigidModelBuilder.Settings
        {
            Fbx = BaseFbx,
            Materials = SlotMaterials(BaseSlots, "PL_Base_Animated", materials),
            Template = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerVisuals.prefab"),
            Folder = PlayerFolder,
            Name = "PL_Base_Visuals",
            Sockets = Sockets,
        });
        var visuals = BuildItemVisuals(materials, items);
        MapUniques(items);
        AssetDatabase.SaveAssets();
        // Opening a scene unloads unused assets: pass paths
        SetUpSubscene(AssetDatabase.GetAssetPath(player), visuals.Select(AssetDatabase.GetAssetPath).ToList());
        AssetDatabase.SaveAssets();
        Debug.Log($"Player equipment built: base {player.name}, {visuals.Count} item visuals.");
    }

    static void ImportBase()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(BaseFbx);
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importAnimation = true;
        importer.isReadable = true;
        var takes = new List<ModelImporterClipAnimation>();
        foreach (var clip in Clips)
        {
            var take = importer.defaultClipAnimations.FirstOrDefault(t => t.takeName.EndsWith("_" + clip));
            if (take == null) throw new System.Exception($"{BaseFbx}: no take ending in _{clip}");
            take.name = clip;
            take.loopTime = LoopingClips.Contains(clip);
            takes.Add(take);
        }
        importer.clipAnimations = takes.ToArray();
        importer.SaveAndReimport();
    }

    static List<EquipmentVisual> BuildItemVisuals(Dictionary<string, Material> materials, Dictionary<string, RollableItem> items)
    {
        var visuals = new List<EquipmentVisual>();
        string slots = $"{ItemModels}/slots.txt";
        foreach (var fbx in Directory.GetFiles(ItemModels, "*.fbx").Select(p => p.Replace('\\', '/')).OrderBy(p => p))
        {
            string itemName = Path.GetFileNameWithoutExtension(fbx);
            if (!items.TryGetValue(itemName, out var item)) { Debug.LogWarning($"No item asset named {itemName} for {fbx}."); continue; }
            var importer = (ModelImporter)AssetImporter.GetAtPath(fbx);
            if (importer.importAnimation || importer.animationType != ModelImporterAnimationType.None)
            {
                importer.importAnimation = false;
                importer.animationType = ModelImporterAnimationType.None;
                importer.SaveAndReimport();
            }

            string visualPath = $"{VisualsFolder}/{itemName}.asset";
            var visual = AssetDatabase.LoadAssetAtPath<EquipmentVisual>(visualPath);
            if (visual == null)
            {
                visual = ScriptableObject.CreateInstance<EquipmentVisual>();
                AssetDatabase.CreateAsset(visual, visualPath);
            }
            visual.Item = item;
            visual.Parts.Clear();
            foreach (var mesh in AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Mesh>().Where(m => m.name.StartsWith(itemName + "__")).OrderBy(m => m.name))
            {
                string socket = mesh.name.Substring(itemName.Length + 2);
                var root = new GameObject(mesh.name);
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                root.AddComponent<MeshRenderer>().sharedMaterials = SlotMaterials(slots, mesh.name, materials);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PartsFolder}/{mesh.name}.prefab");
                Object.DestroyImmediate(root);
                visual.Parts.Add(new EquipmentVisual.Part { Socket = socket, Prefab = prefab });
            }
            EditorUtility.SetDirty(visual);
            visuals.Add(visual);
        }
        return visuals;
    }

    static void MapUniques(Dictionary<string, RollableItem> items)
    {
        foreach (var (unique, baseName) in UniqueBases.Select(p => (p.Key, p.Value)))
        {
            if (!items.TryGetValue(unique, out var item) || !items.TryGetValue(baseName, out var visualBase)) continue;
            // Only fill empty ones, so a choice made in the inspector stays
            if (item.VisualBase != null) continue;
            item.VisualBase = visualBase;
            EditorUtility.SetDirty(item);
        }
    }

    static void SetUpSubscene(string playerPath, List<string> visualPaths)
    {
        var scene = EditorSceneManager.OpenScene(SubscenePath, OpenSceneMode.Additive);
        var player = AssetDatabase.LoadAssetAtPath<GameObject>(playerPath);
        var visuals = visualPaths.Select(AssetDatabase.LoadAssetAtPath<EquipmentVisual>).ToList();

        EquipmentVisualsAuthoring registry = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            var authoring = root.GetComponent<PlayerPrefabAuthoring>();
            if (authoring != null)
            {
                authoring.PlayerVisualPrefab = player;
                PrefabUtility.RecordPrefabInstancePropertyModifications(authoring);
            }
            if (registry == null) registry = root.GetComponent<EquipmentVisualsAuthoring>();
        }
        if (registry == null)
        {
            var go = new GameObject("EquipmentVisuals");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            registry = go.AddComponent<EquipmentVisualsAuthoring>();
        }
        registry.Visuals = visuals;
        EditorUtility.SetDirty(registry);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorSceneManager.CloseScene(scene, true);
    }

    static Material[] SlotMaterials(string slotsPath, string objectName, Dictionary<string, Material> materials)
    {
        var line = File.ReadAllLines(slotsPath).First(l => l.StartsWith(objectName + "|"));
        return line.Split('|')[1].Split(',').Select(n =>
        {
            if (!materials.TryGetValue(n.Trim(), out var m)) throw new FileNotFoundException($"Material {n} for {objectName}");
            return m;
        }).ToArray();
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
        AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
    }
}
