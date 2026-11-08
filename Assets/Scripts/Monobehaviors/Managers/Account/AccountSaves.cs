using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Equipment;
using UnityEngine;

/// <summary>
/// Reads and writes account save data: one folder per account under persistentDataPath/Accounts, named by the account
/// ID. JSON, so it can be read while debugging. Only found items are written. An item is its base item (by Id), seed and
/// roll version: loading rolls it again from those, so balance changes reach saved items and edited values do nothing.
/// The saved rarity and affixes are only there to read; an item whose saved rarity isn't what its seed rolls, that fails
/// ItemValidation, or that shares its Guid is loaded marked Unverified. The account's characters are saved next to it.
/// </summary>
public static class AccountSaves
{
    const string ItemDatabaseFile = "ItemDatabase.json";
    // 2: items are rolled from their seed. Older saves had no seeds; during development their items are dropped.
    const int ItemDatabaseVersion = 2;
    const string CharactersFile = "Characters.json";
    const int CharactersVersion = 1;
    const string StatisticsFile = "Statistics.json";
    const int StatisticsVersion = 1;

    [Serializable]
    class CharactersData
    {
        public int Version;
        public string LastPlayedGuid;
        public List<CharacterData> Characters = new List<CharacterData>();
    }

    [Serializable]
    class CharacterData
    {
        public string Guid;
        public string Name;
        public string Class;
        public string Resource;
        public int Level;
        public int Experience;
        public List<EquippedData> Equipped = new List<EquippedData>();
        public List<CompletedDungeonData> CompletedDungeons = new List<CompletedDungeonData>();
    }

    [Serializable]
    class EquippedData
    {
        public string Slot;
        public string BaseItemId;
        public string Rarity;
    }

    [Serializable]
    class CompletedDungeonData
    {
        public string DungeonId;
        public int Count;
    }

    [Serializable]
    class ItemDatabaseData
    {
        public int Version;
        public List<ItemData> Items = new List<ItemData>();
    }

    [Serializable]
    class ItemData
    {
        public string Guid;
        public string BaseItemId;
        public string BaseItemName;     // Only for reading the file
        public uint Seed;
        public int RollVersion;
        public bool Unverified;
        // Only for reading the file (and to notice an edited rarity); loading rolls them from the seed
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
    /// Rolls the account's saved items from their seeds into the database, which must have its entries already. Items
    /// whose base item isn't in itemsById are skipped with a warning. Returns how many were stored, or -1 when the save
    /// can't be read (it's then copied aside as .unreadable, since the next save replaces it).
    /// </summary>
    public static int ReadItemDatabase(string accountId, ItemDatabase database, IReadOnlyDictionary<string, RollableItem> itemsById,
                                       GenerationSettings settings)
    {
        string path = ItemDatabasePath(accountId);
        if (settings == null)
        {
            Debug.LogError($"Can't load {path}: no GenerationSettings to roll the items with.");
            return -1;
        }
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
            CopyAside(path);
            return -1;
        }

        if (data.Version < ItemDatabaseVersion)
        {
            Debug.LogWarning($"{path} is an older save format (version {data.Version}); its items are removed.");
            WriteItemDatabase(accountId, null);
            return 0;
        }

        var items = new List<Item>();
        foreach (var saved in data.Items)
        {
            var item = FromData(saved, itemsById, settings, path);
            if (item != null) items.Add(item);
        }

        // A Guid is one item: every item sharing one is suspect
        foreach (var group in items.Where(item => !string.IsNullOrEmpty(item.Guid)).GroupBy(item => item.Guid).Where(group => group.Count() > 1))
        {
            foreach (var item in group) item.Unverified = true;
            Debug.LogWarning($"{path}: {group.Count()} items share the Guid {group.Key}; they're marked Unverified.");
        }

        int stored = 0;
        foreach (var item in items)
            if (database.Store(item)) stored++;
        return stored;
    }

    static string CharactersPath(string accountId) => Path.Combine(FolderOf(accountId), CharactersFile);

    /// <summary>Writes the account's characters and which of them was played last (null for none).</summary>
    public static void WriteCharacters(string accountId, IReadOnlyList<AccountCharacter> characters, string lastPlayedGuid)
    {
        var data = new CharactersData { Version = CharactersVersion, LastPlayedGuid = lastPlayedGuid ?? "" };
        foreach (var character in characters)
        {
            var saved = new CharacterData
            {
                Guid = character.Guid,
                Name = character.Name,
                Class = character.Class.ToString(),
                Resource = character.Resource.ToString(),
                Level = character.Level,
                Experience = character.Experience,
            };
            foreach (var equipped in character.Equipped)
                saved.Equipped.Add(new EquippedData { Slot = equipped.Slot, BaseItemId = equipped.BaseItemId, Rarity = equipped.Rarity.ToString() });
            foreach (var completed in character.CompletedDungeons)
                saved.CompletedDungeons.Add(new CompletedDungeonData { DungeonId = completed.DungeonId, Count = completed.Count });
            data.Characters.Add(saved);
        }
        Write(CharactersPath(accountId), JsonUtility.ToJson(data, true));
    }

    /// <summary>
    /// Fills characters with the account's saved characters (none when it has no save yet). False when the save can't be
    /// read (it's then copied aside as .unreadable, since the next save replaces it).
    /// </summary>
    public static bool ReadCharacters(string accountId, List<AccountCharacter> characters, out string lastPlayedGuid)
    {
        characters.Clear();
        lastPlayedGuid = null;
        string path = CharactersPath(accountId);
        if (!File.Exists(path)) return true;

        CharactersData data = null;
        try
        {
            data = JsonUtility.FromJson<CharactersData>(File.ReadAllText(path));
        }
        catch (Exception exception)
        {
            Debug.LogError($"Couldn't read {path}: {exception.Message}");
        }
        if (data == null || data.Characters == null)
        {
            CopyAside(path);
            return false;
        }

        foreach (var saved in data.Characters)
        {
            if (saved == null || string.IsNullOrEmpty(saved.Guid))
            {
                Debug.LogWarning($"{path}: a character without a Guid, skipping it.");
                continue;
            }
            // A class or resource that was renamed or removed: kept, without one
            if (!Enum.TryParse(saved.Class, out ClassType classType))
                Debug.LogWarning($"{path}: {saved.Name} has an unknown class '{saved.Class}'.");
            if (!Enum.TryParse(saved.Resource, out ResourceType resourceType))
                Debug.LogWarning($"{path}: {saved.Name} has an unknown resource '{saved.Resource}'.");
            var character = new AccountCharacter
            {
                Guid = saved.Guid,
                Name = saved.Name,
                Class = classType,
                Resource = resourceType,
                Level = Mathf.Clamp(saved.Level, 1, global::Experience.MaxLevel),
                Experience = Mathf.Max(0, saved.Experience),
            };
            // Saves from before equipment have none; whether the items still exist is checked when they're equipped
            foreach (var equipped in saved.Equipped ?? new List<EquippedData>())
            {
                if (equipped == null || string.IsNullOrEmpty(equipped.Slot) || string.IsNullOrEmpty(equipped.BaseItemId) ||
                    !Enum.TryParse(equipped.Rarity, out Rarity rarity))
                {
                    Debug.LogWarning($"{path}: {saved.Name} has an equipped item that can't be read, skipping it.");
                    continue;
                }
                character.Equipped.Add(new AccountCharacter.EquippedItem { Slot = equipped.Slot, BaseItemId = equipped.BaseItemId, Rarity = rarity });
            }
            // Saves from before dungeons have none
            foreach (var completed in saved.CompletedDungeons ?? new List<CompletedDungeonData>())
            {
                if (completed == null || string.IsNullOrEmpty(completed.DungeonId) || completed.Count <= 0) continue;
                character.CompletedDungeons.Add(new AccountCharacter.CompletedDungeon { DungeonId = completed.DungeonId, Count = completed.Count });
            }
            characters.Add(character);
        }
        lastPlayedGuid = string.IsNullOrEmpty(data.LastPlayedGuid) ? null : data.LastPlayedGuid;
        return true;
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
            Guid = item.Guid,
            BaseItemId = item.BaseItem.Id,
            BaseItemName = item.BaseItem.Name,
            Seed = item.Seed,
            RollVersion = item.RollVersion,
            Unverified = item.Unverified,
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

    // The item rolled again from its seed, marked Unverified when something about it doesn't add up
    static Item FromData(ItemData data, IReadOnlyDictionary<string, RollableItem> itemsById, GenerationSettings settings, string path)
    {
        if (data == null) return null;
        if (string.IsNullOrEmpty(data.BaseItemId) || !itemsById.TryGetValue(data.BaseItemId, out var baseItem))
        {
            Debug.LogWarning($"{path}: no item with ID {data.BaseItemId} ({data.BaseItemName}) can drop anymore, skipping it.");
            return null;
        }
        if (data.RollVersion != RollableItem.CurrentRollVersion)
        {
            Debug.LogWarning($"{path}: {data.BaseItemName} ({data.Guid}) has roll version {data.RollVersion}, which can't be rolled " +
                             $"anymore (current is {RollableItem.CurrentRollVersion}); skipping it.");
            return null;
        }

        var item = baseItem.RollFromSeed(data.Seed, settings);
        item.Guid = data.Guid;
        item.Unverified = data.Unverified;

        var problems = ItemValidation.Check(item, settings);
        if (!Enum.TryParse(data.Rarity, out Rarity savedRarity) || savedRarity != item.Rarity)
            problems.Add($"saved as {data.Rarity}, but its seed rolls {item.Rarity}");
        if (problems.Count > 0)
        {
            item.Unverified = true;
            Debug.LogWarning($"{path}: {data.BaseItemName} ({data.Guid}) is marked Unverified: {string.Join("; ", problems)}.");
        }
        return item;
    }

    // ---------- Statistics ----------

    [Serializable]
    class StatisticsData
    {
        public int Version;
        public List<StatData> Account = new List<StatData>();
        public List<CharacterStatisticsData> Characters = new List<CharacterStatisticsData>();
    }

    [Serializable]
    class CharacterStatisticsData
    {
        public string Guid;
        public List<StatData> Stats = new List<StatData>();
    }

    [Serializable]
    class StatData
    {
        public string Stat;
        public string Subject;
        public string Name;
        public double Value;
    }

    static string StatisticsPath(string accountId) => Path.Combine(FolderOf(accountId), StatisticsFile);

    /// <summary>Writes the account's statistics and its characters'.</summary>
    public static void WriteStatistics(string accountId, AccountStatistics statistics)
    {
        var data = new StatisticsData { Version = StatisticsVersion };
        ToData(statistics.Account, data.Account);
        foreach (var (guid, set) in statistics.Characters)
        {
            var character = new CharacterStatisticsData { Guid = guid };
            ToData(set, character.Stats);
            data.Characters.Add(character);
        }
        Write(StatisticsPath(accountId), JsonUtility.ToJson(data, true));
    }

    /// <summary>
    /// Fills statistics from the account's save (nothing when it has none yet). A stat type that no longer exists is
    /// skipped. False when the save can't be read (it's then copied aside as .unreadable, since the next save replaces it).
    /// </summary>
    public static bool ReadStatistics(string accountId, AccountStatistics statistics)
    {
        string path = StatisticsPath(accountId);
        if (!File.Exists(path)) return true;

        StatisticsData data = null;
        try
        {
            data = JsonUtility.FromJson<StatisticsData>(File.ReadAllText(path));
        }
        catch (Exception exception)
        {
            Debug.LogError($"Couldn't read {path}: {exception.Message}");
        }
        if (data == null)
        {
            CopyAside(path);
            return false;
        }

        FromData(data.Account, statistics.Account, path);
        foreach (var character in data.Characters ?? new List<CharacterStatisticsData>())
            if (character != null && !string.IsNullOrEmpty(character.Guid))
                FromData(character.Stats, statistics.Character(character.Guid), path);
        return true;
    }

    static void ToData(StatisticsSet set, List<StatData> stats)
    {
        foreach (var entry in set.Entries)
            stats.Add(new StatData { Stat = entry.Type.ToString(), Subject = entry.Subject, Name = entry.Name, Value = entry.Value });
    }

    static void FromData(List<StatData> stats, StatisticsSet set, string path)
    {
        foreach (var stat in stats ?? new List<StatData>())
        {
            if (stat == null) continue;
            if (!Enum.TryParse(stat.Stat, out StatType type))
            {
                Debug.LogWarning($"{path}: unknown statistic '{stat.Stat}', skipping it.");
                continue;
            }
            set.Set(type, stat.Subject, stat.Name, stat.Value);
        }
    }

    static void CopyAside(string path)
    {
        try { File.Copy(path, path + ".unreadable", true); }
        catch (Exception exception) { Debug.LogError($"Couldn't copy {path} aside: {exception.Message}"); }
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
