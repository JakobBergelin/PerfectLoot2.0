using System;
using System.Collections.Generic;
using System.Linq;
using Passives;
using UnityEditor;
using UnityEngine;

/// <summary>Makes sure there is one PassiveSkill asset for every PassiveType.</summary>
public static class PassiveSkillAssetCreator
{
    const string Folder = "Assets/Data/Passives";
    const string ManagerPrefabPath = "Assets/Prefabs/PassiveSkillManager.prefab";

    [MenuItem("Tools/Create missing passive assets")]
    static void CreateMissingPassiveAssets()
    {
        // Which PassiveType each concrete PassiveSkill class implements
        var classByType = new Dictionary<PassiveType, Type>();
        foreach (var type in TypeCache.GetTypesDerivedFrom<PassiveSkill>().Where(t => !t.IsAbstract && !t.IsGenericType))
        {
            var probe = (PassiveSkill)ScriptableObject.CreateInstance(type);
            classByType[probe.Type] = type;
            UnityEngine.Object.DestroyImmediate(probe);
        }

        // Which PassiveTypes already have an asset anywhere in the project
        var existing = new HashSet<PassiveType>();
        var allAssets = new List<PassiveSkill>();
        foreach (var type in classByType.Values)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:" + type.Name))
            {
                var asset = AssetDatabase.LoadAssetAtPath<PassiveSkill>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null) continue;
                existing.Add(asset.Type);
                allAssets.Add(asset);
            }
        }

        var created = new List<string>();
        var missingClass = new List<string>();
        foreach (PassiveType passiveType in Enum.GetValues(typeof(PassiveType)))
        {
            if (passiveType == PassiveType.None || existing.Contains(passiveType)) continue;

            if (!classByType.TryGetValue(passiveType, out var type))
            {
                missingClass.Add(passiveType.ToString());
                continue;
            }

            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/Data", "Passives");

            var asset = (PassiveSkill)ScriptableObject.CreateInstance(type);
            asset.Name = passiveType.ToString();
            var path = AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{passiveType}.asset");
            AssetDatabase.CreateAsset(asset, path);
            created.Add(path);
            allAssets.Add(asset);
        }

        AssetDatabase.SaveAssets();

        if (created.Count > 0) Debug.Log("Created passive assets:\n" + string.Join("\n", created));
        else Debug.Log("Every PassiveType already has an asset.");
        if (missingClass.Count > 0)
            Debug.LogWarning("No PassiveSkill class for: " + string.Join(", ", missingClass) + ". Add one under Scripts/ScriptableObjects/Passives.");

        RegisterInManagerPrefab(allAssets);
    }

    /// <summary>Adds any passive asset missing from PassiveSkillManager.AllPassives on the manager prefab.</summary>
    static void RegisterInManagerPrefab(List<PassiveSkill> allAssets)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath) == null)
        {
            Debug.LogWarning($"No prefab at {ManagerPrefabPath}, so AllPassives was not updated.");
            return;
        }

        var root = PrefabUtility.LoadPrefabContents(ManagerPrefabPath);
        try
        {
            var manager = root.GetComponent<PassiveSkillManager>();
            if (manager == null)
            {
                Debug.LogWarning($"{ManagerPrefabPath} has no PassiveSkillManager, so AllPassives was not updated.");
                return;
            }

            var added = allAssets.Where(a => !manager.AllPassives.Contains(a)).ToList();
            if (added.Count == 0) return;

            manager.AllPassives.RemoveAll(a => a == null);
            manager.AllPassives.AddRange(added);
            manager.AllPassives.Sort((a, b) => a.Type.CompareTo(b.Type));
            PrefabUtility.SaveAsPrefabAsset(root, ManagerPrefabPath);
            Debug.Log("Added to PassiveSkillManager.AllPassives: " + string.Join(", ", added.Select(a => a.Type)));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
