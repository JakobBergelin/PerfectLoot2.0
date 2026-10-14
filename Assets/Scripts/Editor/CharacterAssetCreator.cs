using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using UnityEditor;
using UnityEngine;

/// <summary>Makes sure there is one CharacterClass asset per ClassType and one ResourceDefinition asset per ResourceType.</summary>
public static class CharacterAssetCreator
{
    const string ClassFolder = "Assets/Data/Classes";
    const string ResourceFolder = "Assets/Data/ResourceTypes";  // Not "Resources": Unity always ships that folder's contents in builds
    const string ManagerPrefabPath = "Assets/Prefabs/CharacterManager.prefab";

    [MenuItem("Tools/Create missing character assets")]
    static void CreateMissingCharacterAssets()
    {
        var created = new List<string>();
        var classes = CreateMissingClasses(created);
        var resources = CreateMissingResources(created);
        AssetDatabase.SaveAssets();

        if (created.Count > 0) Debug.Log("Created character assets:\n" + string.Join("\n", created));
        else Debug.Log("Every ClassType and ResourceType already has an asset.");

        RegisterInManagerPrefab(classes, resources);
    }

    static List<CharacterClass> CreateMissingClasses(List<string> created)
    {
        var all = LoadAll<CharacterClass>(nameof(CharacterClass));
        var existing = new HashSet<ClassType>(all.Select(c => c.Type));

        foreach (ClassType type in Enum.GetValues(typeof(ClassType)))
        {
            if (type == ClassType.None || existing.Contains(type)) continue;

            var asset = ScriptableObject.CreateInstance<CharacterClass>();
            asset.Type = type;
            asset.Name = type.ToString();
            created.Add(CreateAsset(asset, ClassFolder, type.ToString()));
            all.Add(asset);
        }
        return all;
    }

    static List<ResourceDefinition> CreateMissingResources(List<string> created)
    {
        // Which ResourceType each concrete ResourceDefinition class implements
        var classByType = new Dictionary<ResourceType, Type>();
        foreach (var type in TypeCache.GetTypesDerivedFrom<ResourceDefinition>().Where(t => !t.IsAbstract && !t.IsGenericType))
        {
            var probe = (ResourceDefinition)ScriptableObject.CreateInstance(type);
            classByType[probe.Type] = type;
            UnityEngine.Object.DestroyImmediate(probe);
        }

        var all = new List<ResourceDefinition>();
        foreach (var type in classByType.Values)
            all.AddRange(LoadAll<ResourceDefinition>(type.Name));
        var existing = new HashSet<ResourceType>(all.Select(r => r.Type));

        var missingClass = new List<string>();
        foreach (ResourceType resourceType in Enum.GetValues(typeof(ResourceType)))
        {
            if (resourceType == ResourceType.None || existing.Contains(resourceType)) continue;
            if (!classByType.TryGetValue(resourceType, out var type))
            {
                missingClass.Add(resourceType.ToString());
                continue;
            }

            var asset = (ResourceDefinition)ScriptableObject.CreateInstance(type);
            asset.Name = resourceType.ToString();
            created.Add(CreateAsset(asset, ResourceFolder, resourceType.ToString()));
            all.Add(asset);
        }

        if (missingClass.Count > 0)
            Debug.LogWarning("No ResourceDefinition class for: " + string.Join(", ", missingClass) + ". Add one under Scripts/ScriptableObjects/Character/ResourceTypes.");
        return all;
    }

    static List<T> LoadAll<T>(string typeName) where T : UnityEngine.Object
    {
        var result = new List<T>();
        foreach (var guid in AssetDatabase.FindAssets("t:" + typeName))
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null && !result.Contains(asset)) result.Add(asset);
        }
        return result;
    }

    static string CreateAsset(ScriptableObject asset, string folder, string fileName)
    {
        if (!AssetDatabase.IsValidFolder(folder))
        {
            var slash = folder.LastIndexOf('/');
            AssetDatabase.CreateFolder(folder.Substring(0, slash), folder.Substring(slash + 1));
        }
        var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{fileName}.asset");
        AssetDatabase.CreateAsset(asset, path);
        return path;
    }

    /// <summary>Adds any class/resource asset missing from the CharacterManager prefab's lists.</summary>
    static void RegisterInManagerPrefab(List<CharacterClass> classes, List<ResourceDefinition> resources)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath) == null)
        {
            Debug.LogWarning($"No prefab at {ManagerPrefabPath}, so its lists were not updated.");
            return;
        }

        var root = PrefabUtility.LoadPrefabContents(ManagerPrefabPath);
        try
        {
            var manager = root.GetComponent<CharacterManager>();
            if (manager == null)
            {
                Debug.LogWarning($"{ManagerPrefabPath} has no CharacterManager, so its lists were not updated.");
                return;
            }

            var addedClasses = classes.Where(c => !manager.AllClasses.Contains(c)).ToList();
            var addedResources = resources.Where(r => !manager.AllResources.Contains(r)).ToList();
            if (addedClasses.Count == 0 && addedResources.Count == 0) return;

            manager.AllClasses.RemoveAll(c => c == null);
            manager.AllClasses.AddRange(addedClasses);
            manager.AllClasses.Sort((a, b) => a.Type.CompareTo(b.Type));
            manager.AllResources.RemoveAll(r => r == null);
            manager.AllResources.AddRange(addedResources);
            manager.AllResources.Sort((a, b) => a.Type.CompareTo(b.Type));
            PrefabUtility.SaveAsPrefabAsset(root, ManagerPrefabPath);

            Debug.Log("Added to CharacterManager: " + string.Join(", ", addedClasses.Select(c => c.Type.ToString()).Concat(addedResources.Select(r => r.Type.ToString()))));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
