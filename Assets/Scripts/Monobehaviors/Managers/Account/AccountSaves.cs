using System;
using System.Collections.Generic;
using System.IO;
using Equipment;
using UnityEngine;

/// <summary>
/// Reads and writes account save data: one folder per account under persistentDataPath/Accounts, named by the account
/// ID. JSON, so it can be read while debugging. Only found items are written, pointing to their base item by Id; rarity
/// and affixes are saved by name, so reordering those enums can't change a saved item.
/// </summary>
public static class AccountSaves
{
    const string ItemDatabaseFile = "ItemDatabase.json";
    const int ItemDatabaseVersion = 1;

    [Serializable]
    class ItemDatabaseData
    {
        public int Version;
        public List<ItemData> Items = new List<ItemData>();
    }

    [Serializable]
    class ItemData
    {
        public string BaseItemId;
        public string BaseItemName;     // Only for reading the file
        public string Rarity;
        public List<AffixData> BaseAffixes = new List<AffixData>();
        public List<AffixData> RollableAffixes = new List<AffixData>();
    }

    [Serializable]
    class AffixData
    {
        public string Affix;
        public float Value;
    }

    public static string RootFolder => Path.Combine(Application.persistentDataPath, "Accounts");

    public static string FolderOf(string accountId) => Path.Combine(RootFolder, accountId);

    static string ItemDatabasePath(string accountId) => Path.Combine(FolderOf(accountId), ItemDatabaseFile);

    public static bool HasItemDatabase(string accountId) => File.Exists(ItemDatabasePath(accountId));

    /// <summary>Writes the database's found items (none for a null database: a new account's empty database).</summary>
    public static void WriteItemDatabase(string accountId, ItemDatabase database)
    {
        var data = new ItemDatabaseData { Version = ItemDatabaseVersion };
        if (database != null)
        {
            foreach (var entry in database.Entries)
                if (!entry.IsEmpty) data.Items.Add(ToData(entry.Item));
        }
        Write(ItemDatabasePath(accountId), JsonUtility.ToJson(data, true));
    }

    /// <summary>
    /// Stores the account's saved items in the database, which must have its entries already. Items whose base item
    /// isn't in itemsById are skipped with a warning. Returns how many were stored, or -1 when the save can't be read
    /// (it's then copied aside as .unreadable, since the next save replaces it).
    /// </summary>
    public static int ReadItemDatabase(string accountId, ItemDatabase database, IReadOnlyDictionary<string, RollableItem> itemsById)
    {
        string path = ItemDatabasePath(accountId);
        ItemDatabaseData data = null;
        try
        {
            data = JsonUtility.FromJson<ItemDatabaseData>(File.ReadAllText(path));
        }
        catch (Exception exception)
        {
            Debug.LogError($"Couldn't read {path}: {exception.Message}");
        }
        if (data == null || data.Items == null)
        {
            try { File.Copy(path, path + ".unreadable", true); }
            catch (Exception exception) { Debug.LogError($"Couldn't copy {path} aside: {exception.Message}"); }
            return -1;
        }

        int stored = 0;
        foreach (var saved in data.Items)
        {
            var item = FromData(saved, itemsById, path);
            if (item != null && database.Store(item)) stored++;
        }
        return stored;
    }

    /// <summary>Deletes everything saved for the account. False if there was nothing.</summary>
    public static bool DeleteAccount(string accountId)
    {
        string folder = FolderOf(accountId);
        if (!Directory.Exists(folder)) return false;
        Directory.Delete(folder, true);
        return true;
    }

    static ItemData ToData(Item item)
    {
        var data = new ItemData
        {
            BaseItemId = item.BaseItem.Id,
            BaseItemName = item.BaseItem.Name,
            Rarity = item.Rarity.ToString(),
        };
        AddAffixes(item.BaseAffixes, data.BaseAffixes);
        AddAffixes(item.RollableAffixes, data.RollableAffixes);
        return data;
    }

    static void AddAffixes(List<Rolled> affixes, List<AffixData> data)
    {
        if (affixes == null) return;
        foreach (var rolled in affixes)
            if (rolled != null) data.Add(new AffixData { Affix = rolled.Affix.ToString(), Value = rolled.Value });
    }

    static Item FromData(ItemData data, IReadOnlyDictionary<string, RollableItem> itemsById, string path)
    {
        if (data == null) return null;
        if (string.IsNullOrEmpty(data.BaseItemId) || !itemsById.TryGetValue(data.BaseItemId, out var baseItem))
        {
            Debug.LogWarning($"{path}: no item with ID {data.BaseItemId} ({data.BaseItemName}) can drop anymore, skipping it.");
            return null;
        }
        if (!Enum.TryParse(data.Rarity, out Rarity rarity))
        {
            Debug.LogWarning($"{path}: {data.BaseItemName} has an unknown rarity '{data.Rarity}', skipping it.");
            return null;
        }

        var item = baseItem.CreateItem();
        item.Rarity = rarity;
        ReadAffixes(data.BaseAffixes, item.BaseAffixes, data, path);
        ReadAffixes(data.RollableAffixes, item.RollableAffixes, data, path);
        return item;
    }

    static void ReadAffixes(List<AffixData> data, List<Rolled> affixes, ItemData item, string path)
    {
        if (data == null) return;
        foreach (var saved in data)
        {
            if (saved != null && Enum.TryParse(saved.Affix, out Affix affix))
                affixes.Add(new Rolled { Affix = affix, Value = saved.Value });
            else
                Debug.LogWarning($"{path}: {item.BaseItemName} has an unknown affix '{saved?.Affix}', leaving it out.");
        }
    }

    // Through a temporary file, so a crash mid-write can't leave half a save
    static void Write(string path, string json)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, json);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }
        catch (Exception exception)
        {
            Debug.LogError($"Couldn't save {path}: {exception.Message}");
        }
    }
}
