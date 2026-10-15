using System;
using System.Collections.Generic;
using System.Linq;
using ActiveSkills;
using UnityEditor;
using UnityEngine;

/// <summary>Makes sure there is one ActiveSkill asset per SkillType, and that ActiveSkillManager lists them all.</summary>
public static class ActiveSkillAssetCreator
{
    const string Folder = "Assets/Data/ActiveSkills";
    const string ManagerPrefabPath = "Assets/Prefabs/ActiveSkillManager.prefab";

    [MenuItem("Tools/Create missing active skill assets")]
    static void CreateMissingActiveSkillAssets()
    {
        // Which SkillType each concrete ActiveSkill class implements
        var classByType = new Dictionary<SkillType, Type>();
        foreach (var type in TypeCache.GetTypesDerivedFrom<ActiveSkill>().Where(t => !t.IsAbstract && !t.IsGenericType))
        {
            var probe = (ActiveSkill)ScriptableObject.CreateInstance(type);
            classByType[probe.Type] = type;
            UnityEngine.Object.DestroyImmediate(probe);
        }

        var all = new List<ActiveSkill>();
        foreach (var type in classByType.Values)
            foreach (var guid in AssetDatabase.FindAssets("t:" + type.Name))
            {
                var asset = AssetDatabase.LoadAssetAtPath<ActiveSkill>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && !all.Contains(asset)) all.Add(asset);
            }
        var existing = new HashSet<SkillType>(all.Select(s => s.Type));

        var created = new List<string>();
        var missingClass = new List<string>();
        foreach (SkillType skillType in Enum.GetValues(typeof(SkillType)))
        {
            if (existing.Contains(skillType)) continue;
            if (!classByType.TryGetValue(skillType, out var type))
            {
                missingClass.Add(skillType.ToString());
                continue;
            }

            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/Data", "ActiveSkills");

            var asset = (ActiveSkill)ScriptableObject.CreateInstance(type);
            asset.Name = skillType.ToString();
            var path = AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{skillType}.asset");
            AssetDatabase.CreateAsset(asset, path);
            created.Add(path);
            all.Add(asset);
        }
        AssetDatabase.SaveAssets();

        if (created.Count > 0) Debug.Log("Created active skill assets:\n" + string.Join("\n", created));
        else Debug.Log("Every SkillType already has an asset.");
        if (missingClass.Count > 0)
            Debug.LogWarning("No ActiveSkill class for: " + string.Join(", ", missingClass) + ". Add one under Scripts/ScriptableObjects/ActiveSkills/Skills.");

        RegisterInManagerPrefab(all);
    }

    static void RegisterInManagerPrefab(List<ActiveSkill> all)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath) == null)
        {
            Debug.LogWarning($"No prefab at {ManagerPrefabPath}, so AllSkills was not updated.");
            return;
        }

        var root = PrefabUtility.LoadPrefabContents(ManagerPrefabPath);
        try
        {
            var manager = root.GetComponent<ActiveSkillManager>();
            if (manager == null)
            {
                Debug.LogWarning($"{ManagerPrefabPath} has no ActiveSkillManager, so AllSkills was not updated.");
                return;
            }

            var added = all.Where(s => !manager.AllSkills.Contains(s)).ToList();
            if (added.Count == 0) return;

            manager.AllSkills.RemoveAll(s => s == null);
            manager.AllSkills.AddRange(added);
            manager.AllSkills.Sort((a, b) => a.Type.CompareTo(b.Type));
            PrefabUtility.SaveAsPrefabAsset(root, ManagerPrefabPath);
            Debug.Log("Added to ActiveSkillManager.AllSkills: " + string.Join(", ", added.Select(s => s.Type)));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
