using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Equipment;
using Loottables;
using UnityEditor;
using UnityEngine;

/// <summary>Finding, creating, duplicating, deleting and renaming item assets, and their loot table membership.</summary>
public static class ItemAssets
{
    public const string ItemsFolder = "Assets/Data/Items";

    public enum Tier { Standard, Legendary, Unique }

    public static Tier TierOf(RollableItem item) => item.ForcedRarity switch
    {
        Rarity.Legendary => Tier.Legendary,
        Rarity.Unique => Tier.Unique,
        _ => Tier.Standard,
    };

    /// <summary>The rarity used for colors: Legendary/Unique as forced, standard items as Normal.</summary>
    public static Rarity DisplayRarity(RollableItem item) => item.ForcedRarity ?? Rarity.Normal;

    public static List<RollableItem> FindAllItems() => FindAll<RollableItem>();
    public static List<LootTable> FindAllLootTables() => FindAll<LootTable>();

    static List<T> FindAll<T>() where T : UnityEngine.Object =>
        AssetDatabase.FindAssets($"t:{typeof(T).Name}")
            .Select(guid => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(asset => asset != null)
            .ToList();

    /// <summary>The concrete item class for a slot and tier (found automatically, so new item classes just work).</summary>
    public static Type ItemClassFor(ItemType slot, Tier tier)
    {
        foreach (var type in TypeCache.GetTypesDerivedFrom<RollableItem>())
        {
            if (type.IsAbstract) continue;
            var probe = (RollableItem)ScriptableObject.CreateInstance(type);
            bool matches = probe.ItemType == slot && TierOf(probe) == tier;
            UnityEngine.Object.DestroyImmediate(probe);
            if (matches) return type;
        }
        return null;
    }

    public static RollableItem Create(string name, ItemType slot, Tier tier)
    {
        var type = ItemClassFor(slot, tier);
        if (type == null)
        {
            Debug.LogError($"Item Editor: there is no item class for a {tier} {slot}.");
            return null;
        }

        string folder = $"{ItemsFolder}/{slot}";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(ItemsFolder, slot.ToString());

        var item = (RollableItem)ScriptableObject.CreateInstance(type);
        item.Name = name;
        item.BaseAffixes = new List<Rollable>();
        item.RollableAffixes = new List<Rollable>();
        string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{FileName(name)}.asset");
        AssetDatabase.CreateAsset(item, path);
        AssetDatabase.SaveAssets();
        return item;
    }

    public static RollableItem Duplicate(RollableItem item)
    {
        string path = AssetDatabase.GetAssetPath(item);
        string copyName = item.Name + " Copy";
        string copyPath = AssetDatabase.GenerateUniqueAssetPath($"{Path.GetDirectoryName(path)}/{FileName(copyName)}.asset");
        if (!AssetDatabase.CopyAsset(path, copyPath)) return null;

        var copy = AssetDatabase.LoadAssetAtPath<RollableItem>(copyPath);
        copy.Name = copyName;
        EditorUtility.SetDirty(copy);
        AssetDatabase.SaveAssets();
        return copy;
    }

    /// <summary>Deletes the asset after removing it from every loot table, so none keeps a dangling entry.</summary>
    public static void Delete(RollableItem item, IEnumerable<LootTable> lootTables)
    {
        foreach (var table in lootTables)
            if (Contains(table, item)) SetIncluded(table, item, false, 0f);
        AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(item));
        AssetDatabase.SaveAssets();
    }

    /// <summary>Renames the asset file to match the item's name. Returns an error message, or null.</summary>
    public static string RenameAssetToMatch(RollableItem item)
    {
        string fileName = FileName(item.Name);
        if (string.IsNullOrEmpty(fileName) || fileName == item.name) return null;
        string error = AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(item), fileName);
        return string.IsNullOrEmpty(error) ? null : error;
    }

    /// <summary>A valid file name from an item name: invalid characters and spaces removed.</summary>
    public static string FileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "NewItem";
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Where(c => !invalid.Contains(c) && c != ' ').ToArray());
    }

    // ---------- Copying between items ----------

    /// <summary>Items that have a field with this name and type, so a value of it can be copied to them.</summary>
    public static List<RollableItem> FieldTargets(RollableItem source, string fieldName, IEnumerable<RollableItem> items)
    {
        var sourceField = source.GetType().GetField(fieldName);
        if (sourceField == null) return new List<RollableItem>();
        return items.Where(item => item != null && item != source &&
                                   item.GetType().GetField(fieldName) is { } field && field.FieldType == sourceField.FieldType)
                    .ToList();
    }

    /// <summary>Copies one serialized field (by property path) from source onto every target. One undo step.</summary>
    public static void CopyField(RollableItem source, string propertyPath, IEnumerable<RollableItem> targets)
    {
        var sourceProperty = new SerializedObject(source).FindProperty(propertyPath);
        if (sourceProperty == null) return;
        int group = BeginUndo($"Copy {sourceProperty.displayName}");
        foreach (var target in targets)
        {
            var serialized = new SerializedObject(target);
            serialized.CopyFromSerializedProperty(sourceProperty);
            serialized.ApplyModifiedProperties();
        }
        Undo.CollapseUndoOperations(group);
    }

    /// <summary>
    /// Copies an affix range into the same list (BaseAffixes / RollableAffixes) on every target: replaces the range if the
    /// target already has that affix there, otherwise adds it. One undo step.
    /// </summary>
    public static void CopyAffix(string listName, Affix affix, float min, float max, IEnumerable<RollableItem> targets)
    {
        int group = BeginUndo($"Copy {affix}");
        foreach (var target in targets)
        {
            var serialized = new SerializedObject(target);
            var list = serialized.FindProperty(listName);
            if (list == null) continue;

            SerializedProperty element = null;
            for (int i = 0; i < list.arraySize; i++)
            {
                var candidate = list.GetArrayElementAtIndex(i);
                if (candidate.FindPropertyRelative(nameof(Rollable.Affix)).intValue == (int)affix) { element = candidate; break; }
            }
            if (element == null)
            {
                list.arraySize++;
                element = list.GetArrayElementAtIndex(list.arraySize - 1);
                element.FindPropertyRelative(nameof(Rollable.Affix)).intValue = (int)affix;
            }
            var range = element.FindPropertyRelative(nameof(Rollable.RollRange));
            range.FindPropertyRelative(nameof(RollRange.Min)).floatValue = min;
            range.FindPropertyRelative(nameof(RollRange.Max)).floatValue = max;
            serialized.ApplyModifiedProperties();
        }
        Undo.CollapseUndoOperations(group);
    }

    static int BeginUndo(string name)
    {
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(name);
        return Undo.GetCurrentGroup();
    }

    // ---------- Rarity tier ----------

    /// <summary>
    /// Changes an item's tier by switching its asset to the item class for that tier. The asset keeps its GUID and file
    /// (loot tables and other references stay valid) and every field the classes share. Can't be undone.
    /// Returns the reloaded item.
    /// </summary>
    public static RollableItem ChangeTier(RollableItem item, Tier tier)
    {
        var type = ItemClassFor(item.ItemType, tier);
        if (type == null || type == item.GetType()) return item;

        var probe = ScriptableObject.CreateInstance(type);
        var script = MonoScript.FromScriptableObject(probe);
        UnityEngine.Object.DestroyImmediate(probe);
        if (script == null)
        {
            Debug.LogError($"Item Editor: no script asset found for {type.Name}.");
            return item;
        }

        string path = AssetDatabase.GetAssetPath(item);
        var serialized = new SerializedObject(item);
        serialized.FindProperty("m_Script").objectReferenceValue = script;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(item);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<RollableItem>(path);
    }

    // ---------- Loot tables ----------

    public static bool Contains(LootTable table, RollableItem item) => IndexOf(table, item) >= 0;

    public static int IndexOf(LootTable table, RollableItem item) =>
        table.Entries == null ? -1 : table.Entries.FindIndex(entry => entry != null && entry.Entry == item);

    public static void SetIncluded(LootTable table, RollableItem item, bool included, float weight)
    {
        Undo.RecordObject(table, included ? "Add item to loot table" : "Remove item from loot table");
        table.Entries ??= new List<LootEntry>();
        if (included)
        {
            if (!Contains(table, item)) table.Entries.Add(new LootEntry { Entry = item, Value = weight });
        }
        else
        {
            table.Entries.RemoveAll(entry => entry != null && entry.Entry == item);
        }
        EditorUtility.SetDirty(table);
    }

    public static void SetWeight(LootTable table, RollableItem item, float weight)
    {
        int index = IndexOf(table, item);
        if (index < 0) return;
        Undo.RecordObject(table, "Change loot weight");
        table.Entries[index].Value = weight;
        EditorUtility.SetDirty(table);
    }

    /// <summary>The loot table most items of this slot are in (used as the default for new items).</summary>
    public static LootTable DefaultTableFor(ItemType slot, IEnumerable<LootTable> tables) =>
        tables.OrderByDescending(table => table.Entries?.Count(entry => entry?.Entry is RollableItem rollable && rollable.ItemType == slot) ?? 0)
              .FirstOrDefault(table => table.Entries != null && table.Entries.Any(entry => entry?.Entry is RollableItem rollable && rollable.ItemType == slot));
}
