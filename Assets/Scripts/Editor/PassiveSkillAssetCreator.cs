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
        foreach (var type in classByType.Values)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:" + type.Name))
            {
                var asset = AssetDatabase.LoadAssetAtPath<PassiveSkill>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) existing.Add(asset.Type);
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
        }

        AssetDatabase.SaveAssets();

        if (created.Count > 0) Debug.Log("Created passive assets:\n" + string.Join("\n", created));
        else Debug.Log("Every PassiveType already has an asset.");
        if (missingClass.Count > 0)
            Debug.LogWarning("No PassiveSkill class for: " + string.Join(", ", missingClass) + ". Add one under Scripts/ScriptableObjects/Passives.");
    }
}
