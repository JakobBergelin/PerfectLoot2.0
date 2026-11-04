using System.Collections.Generic;
using System.IO;
using System.Linq;
using Enemies;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the common enemies from ArtSource/Enemies_Base.blend's exports (pl_enemies.py): Generic import with named
/// clips, their PerfectLoot/Comic materials, rigid visual prefabs (the spider on the swarm template) with their hit and
/// death effects, and points the Skeleton, Zombie, Spider and Dummy definitions at them.
/// Safe to run again; prefabs keep their GUIDs, material colours are only set when a material is first made.
/// </summary>
public static class EnemySetup
{
    const string Models = "Assets/ArtStyleTest/Models/Enemies/Animated";
    const string MaterialsFolder = "Assets/ArtStyleTest/Materials/Enemies";
    const string VisualsFolder = "Assets/Prefabs/Enemies";
    static readonly string[] SharedMaterialFolders = { "Assets/ArtStyleTest/Materials/ComicCathedral", MaterialsFolder };
    static readonly string[] Clips = { "Idle", "Move", "Attack", "Hit", "Death" };

    // New materials: copies of a character material (or a glowing one), recoloured. Base, emission (null: none)
    static readonly Dictionary<string, (string Base, string Emission)> NewMaterials = new Dictionary<string, (string, string)>
    {
        ["EN_Bone"] = ("#E2D8BE", null), ["EN_BoneDark"] = ("#8A7E6A", null),
        ["EN_ZombieSkin"] = ("#7E9A68", null), ["EN_ZombieFlesh"] = ("#5A3A44", null), ["EN_ZombieRags"] = ("#8A7A62", null),
        ["EN_SpiderChitin"] = ("#8E6A5A", null), ["EN_SpiderMark"] = ("#C8447E", "#FF5FA8"),
        ["EN_Burlap"] = ("#B89A6A", null), ["EN_Straw"] = ("#D8C07A", null),
    };

    class Enemy
    {
        public string Definition, Model, Template;
        public VfxType Hit = VfxType.UnitHit, Death = VfxType.UndeadDeath;
    }

    static readonly Enemy[] Enemies =
    {
        new Enemy { Definition = "Assets/Data/Enemies/Skeleton.asset", Model = "EN_Skeleton", Template = "Assets/Prefabs/EnemyVisuals.prefab" },
        new Enemy { Definition = "Assets/Data/Enemies/Zombie.asset", Model = "EN_Zombie", Template = "Assets/Prefabs/EnemyVisuals.prefab" },
        new Enemy { Definition = "Assets/Data/Enemies/Spider.asset", Model = "EN_Spider", Template = "Assets/Prefabs/EnemySwarmVisuals.prefab", Death = VfxType.UnitDeath },
        new Enemy { Definition = "Assets/Data/Enemies/Dummy.asset", Model = "EN_Dummy", Template = "Assets/Prefabs/EnemyVisuals.prefab", Death = VfxType.UnitDeath },
    };

    [MenuItem("Tools/Enemies/Build Common Enemies")]
    public static void Build()
    {
        EnsureFolder(MaterialsFolder);
        EnsureFolder(VisualsFolder);
        AssetDatabase.Refresh();
        CreateMaterials();
        var materials = SharedMaterialFolders.SelectMany(f => AssetDatabase.FindAssets("t:Material", new[] { f }))
            .Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g)))
            .GroupBy(m => m.name).ToDictionary(g => g.Key, g => g.First());

        foreach (var enemy in Enemies)
        {
            string fbx = $"{Models}/{enemy.Model}_Animated.fbx";
            Import(fbx);
            var prefab = RigidModelBuilder.Build(new RigidModelBuilder.Settings
            {
                Fbx = fbx,
                Materials = SlotMaterials($"{Models}/slots.txt", enemy.Model + "_Animated", materials),
                Template = AssetDatabase.LoadAssetAtPath<GameObject>(enemy.Template),
                Folder = VisualsFolder,
                Name = enemy.Model + "_Visuals",
            });
            // The hit and death effects go on the prefab root
            string path = AssetDatabase.GetAssetPath(prefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            var vfx = root.GetComponent<UnitVfxAuthoring>();
            if (vfx == null) vfx = root.AddComponent<UnitVfxAuthoring>();
            vfx.Hit = enemy.Hit;
            vfx.Death = enemy.Death;
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);

            var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(enemy.Definition);
            definition.Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            definition.OverrideColor = false;   // The tint was for the capsule; the model has its own colours
            EditorUtility.SetDirty(definition);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"Common enemies built: {string.Join(", ", Enemies.Select(e => e.Model))}.");
    }

    static void CreateMaterials()
    {
        var character = AssetDatabase.LoadAssetAtPath<Material>("Assets/ArtStyleTest/Materials/ComicCathedral/CT_Pale.mat");
        var glowing = AssetDatabase.LoadAssetAtPath<Material>("Assets/ArtStyleTest/Materials/ComicCathedral/CT_GhostEye.mat");
        foreach (var (name, colours) in NewMaterials.Select(p => (p.Key, p.Value)))
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) continue;
            var material = new Material(colours.Emission != null ? glowing : character);
            material.SetColor("_BaseColor", Hex(colours.Base));
            if (colours.Emission != null) material.SetColor("_EmissionColor", Hex(colours.Emission) * 1.2f);
            AssetDatabase.CreateAsset(material, path);
        }
    }

    static void Import(string fbx)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(fbx);
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importAnimation = true;
        importer.isReadable = true;
        var takes = new List<ModelImporterClipAnimation>();
        foreach (var clip in Clips)
        {
            var take = importer.defaultClipAnimations.FirstOrDefault(t => t.takeName.EndsWith("_" + clip));
            if (take == null) continue;   // The Dummy only has Idle and Hit
            take.name = clip;
            take.loopTime = clip == "Idle" || clip == "Move";
            takes.Add(take);
        }
        importer.clipAnimations = takes.ToArray();
        importer.SaveAndReimport();
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

    static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
        AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
    }
}
