using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Equipment;
using Loottables;
using UnityEngine;

/// <summary>
/// Account-wide state that outlives scenes: who is playing (the name given in the main menu, standing in for a real
/// account and used as the ID for saved data), the account's characters and which one is played, the item database
/// shared by all characters, and the queue of dropped items waiting for the player (DropPopupHUD). Play mode only for now.
/// </summary>
public class AccountManager : MonoBehaviour
{
    public enum DropKind
    {
        NewItem,    // The entry was empty: the item is already stored, the popup just shows it
        Compare,    // The entry has an item: the player takes the new one or keeps the current one
    }

    public class PendingDrop
    {
        public Item Item;
        public DropKind Kind;
        public ItemKey Key => ItemKey.Of(Item);
    }

    public static AccountManager Instance;

    // ---------- Account ----------

    /// <summary>The account used when a game scene is played without going through the main menu.</summary>
    public const string DefaultAccountName = "Dev";
    public const int MaxNameLength = 20;
    const string LastNameKey = "PerfectLoot.LastAccountName";

    string _accountName = DefaultAccountName;

    /// <summary>The name as the player typed it.</summary>
    public string AccountName => _accountName;

    /// <summary>The ID for saved data: the name in lowercase, so "Matt" and "matt" are the same account.</summary>
    public string AccountId => ToAccountId(_accountName);

    /// <summary>The name last signed in with (on this machine), or empty.</summary>
    public static string LastAccountName => CleanName(PlayerPrefs.GetString(LastNameKey, ""));

    public static bool IsValidNameCharacter(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_';

    /// <summary>The name without the characters a name can't have, at most MaxNameLength long.</summary>
    public static string CleanName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var clean = new StringBuilder(name.Length);
        foreach (char c in name)
            if (IsValidNameCharacter(c) && clean.Length < MaxNameLength) clean.Append(c);
        return clean.ToString();
    }

    public static bool IsValidName(string name) => !string.IsNullOrEmpty(name) && CleanName(name) == name;

    public static string ToAccountId(string name) => name.ToLowerInvariant();

    /// <summary>Raised after SignIn changed the account.</summary>
    public event Action AccountChanged;

    /// <summary>
    /// Plays as this account from now on and remembers the name for the next launch. A new account gets an empty item
    /// database saved. The database is loaded for the account the next time it's used. False if the name isn't valid.
    /// </summary>
    public bool SignIn(string name)
    {
        if (!IsValidName(name)) return false;
        _accountName = name;
        PlayerPrefs.SetString(LastNameKey, name);
        PlayerPrefs.Save();

        if (!AccountSaves.HasItemDatabase(AccountId)) AccountSaves.WriteItemDatabase(AccountId, null);
        _itemsInitialized = false;
        _pendingDrops.Clear();
        _charactersLoaded = false;
        _characters.Clear();
        SelectedCharacter = null;
        AccountChanged?.Invoke();
        PendingDropsChanged?.Invoke();
        CharactersChanged?.Invoke();
        return true;
    }

    // ---------- Characters ----------

    readonly List<AccountCharacter> _characters = new List<AccountCharacter>();
    bool _charactersLoaded;
    string _lastPlayedGuid;

    /// <summary>The account's characters, oldest first. Loaded from the account's save on first use.</summary>
    public IReadOnlyList<AccountCharacter> Characters
    {
        get
        {
            EnsureCharactersLoaded();
            return _characters;
        }
    }

    /// <summary>The character the game scene plays, or null (e.g. a game scene played without the main menu).</summary>
    public AccountCharacter SelectedCharacter { get; private set; }

    /// <summary>The character the account last went on an adventure with, or null.</summary>
    public AccountCharacter LastPlayedCharacter => FindCharacter(_lastPlayedGuid);

    /// <summary>Raised after a character was created or deleted, or the account changed.</summary>
    public event Action CharactersChanged;

    public AccountCharacter FindCharacter(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return null;
        foreach (var character in Characters)
            if (character.Guid == guid) return character;
        return null;
    }

    /// <summary>Whether a character on the account already has this name, ignoring case.</summary>
    public bool IsCharacterNameTaken(string name)
    {
        foreach (var character in Characters)
            if (string.Equals(character.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// Adds a level 1 character to the account with its class's starting items equipped, and saves it. Null when the
    /// name isn't valid (same rules as account names) or taken, or the class or resource is None.
    /// </summary>
    public AccountCharacter CreateCharacter(string name, ClassType classType, ResourceType resource,
                                            IReadOnlyList<RollableItem> startingItems = null)
    {
        if (!IsValidName(name) || IsCharacterNameTaken(name) || classType == ClassType.None || resource == ResourceType.None) return null;
        var character = new AccountCharacter
        {
            Guid = Guid.NewGuid().ToString("N"),
            Name = name,
            Class = classType,
            Resource = resource,
            Level = 1,
        };
        GiveStartingItems(character, startingItems);
        _characters.Add(character);
        SaveCharacters();
        CharactersChanged?.Invoke();
        return character;
    }

    /// <summary>Replaces what the character has equipped and saves (InventoryManager, after a slot changed).</summary>
    public void SetEquipped(AccountCharacter character, IEnumerable<AccountCharacter.EquippedItem> equipped)
    {
        if (character == null || !_characters.Contains(character)) return;
        character.Equipped = new List<AccountCharacter.EquippedItem>(equipped);
        SaveCharacters();
    }

    // Equips a Normal item of each kind: the account's own if it has found one, otherwise a new one rolled into the
    // database. Each goes into the first free slot of its type.
    void GiveStartingItems(AccountCharacter character, IReadOnlyList<RollableItem> startingItems)
    {
        if (startingItems == null || startingItems.Count == 0) return;
        var loot = LootGenerationManager.Instance;
        if (loot == null)
        {
            Debug.LogWarning($"AccountManager: no LootGenerationManager, so {character.Name} gets no starting items. " +
                             "Run Tools → Create Main Menu Scene to add one to the menu.", this);
            return;
        }

        foreach (var baseItem in startingItems)
        {
            if (baseItem == null) continue;
            if (string.IsNullOrEmpty(baseItem.Id))
            {
                Debug.LogWarning($"AccountManager: starting item {baseItem.name} has no Id. Run Tools → Assign Item IDs.", this);
                continue;
            }
            var key = new ItemKey(baseItem, Rarity.Normal);
            if (!Items.Contains(key))
            {
                Debug.LogWarning($"AccountManager: starting item {key} isn't in the item database (not in a loot table, or can't be Normal).", this);
                continue;
            }
            if (character.Equipped.Any(equipped => equipped.BaseItemId == baseItem.Id && equipped.Rarity == Rarity.Normal)) continue;
            if (Items.Get(key) == null && !Items.Store(loot.RollItem(baseItem, Rarity.Normal))) continue;

            string slot = InventoryManager.SlotNamesFor(baseItem.ItemType).FirstOrDefault(name => character.Equipped.All(equipped => equipped.Slot != name));
            if (slot == null)
            {
                Debug.LogWarning($"AccountManager: no free {baseItem.ItemType} slot for {character.Name}'s starting item {key}.", this);
                continue;
            }
            character.Equipped.Add(new AccountCharacter.EquippedItem { Slot = slot, BaseItemId = baseItem.Id, Rarity = Rarity.Normal });
        }
    }

    /// <summary>Removes the character from the account and saves. False if the account has no such character.</summary>
    public bool DeleteCharacter(string guid)
    {
        var character = FindCharacter(guid);
        if (character == null) return false;
        _characters.Remove(character);
        if (_lastPlayedGuid == guid) _lastPlayedGuid = null;
        if (SelectedCharacter == character) SelectedCharacter = null;
        SaveCharacters();
        CharactersChanged?.Invoke();
        return true;
    }

    /// <summary>Plays this character in the next game scene and remembers it as last played. False if there's no such character.</summary>
    public bool SelectCharacter(string guid)
    {
        var character = FindCharacter(guid);
        if (character == null) return false;
        SelectedCharacter = character;
        _lastPlayedGuid = guid;
        SaveCharacters();
        return true;
    }

    void EnsureCharactersLoaded()
    {
        if (_charactersLoaded) return;
        _charactersLoaded = true;
        // An unreadable save leaves the account without characters; it was copied aside before the next save replaces it
        AccountSaves.ReadCharacters(AccountId, _characters, out _lastPlayedGuid);
    }

    void SaveCharacters() => AccountSaves.WriteCharacters(AccountId, _characters, _lastPlayedGuid);

    [SerializeField, Tooltip("Read-only: one entry per item kind and rarity, empty until found")]
    ItemDatabase _items = new ItemDatabase();
    bool _itemsInitialized;
    bool _loadingItems;     // Storing saved items doesn't save them again

    // RollableItem.Id -> item kind, for every kind in the database
    readonly Dictionary<string, RollableItem> _itemsById = new Dictionary<string, RollableItem>();

    // Item kind -> names of the loot tables that contain it
    readonly Dictionary<RollableItem, List<string>> _sources = new Dictionary<RollableItem, List<string>>();

    readonly List<PendingDrop> _pendingDrops = new List<PendingDrop>();

    /// <summary>Dropped items waiting for the player, oldest first.</summary>
    public IReadOnlyList<PendingDrop> PendingDrops => _pendingDrops;

    /// <summary>Raised after a drop was queued or resolved.</summary>
    public event Action PendingDropsChanged;

    /// <summary>
    /// The account's item database. Built on first use from every item in LootGenerationManager's loot tables (items
    /// that can't drop get no entries), so it's ready no matter which manager's Start runs first, and then filled from
    /// the account's save. Saved again after every change.
    /// </summary>
    public ItemDatabase Items
    {
        get
        {
            if (!_itemsInitialized) InitializeItems();
            return _items;
        }
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        _items.ItemChanged += OnItemChanged;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void InitializeItems()
    {
        var loot = LootGenerationManager.Instance;
        if (loot == null)
        {
            Debug.LogWarning("AccountManager: no LootGenerationManager yet, so the item database has no entries.", this);
            return;
        }

        _sources.Clear();
        var visited = new HashSet<LootTable>();
        foreach (var table in loot.AllLootTables)
            CollectItems(table, visited);

        _items.Initialize(_sources.Keys);
        LoadItems(loot.GenerationSettings);
        _itemsInitialized = true;
    }

    // Fills the freshly built database from the account's save (a new account gets its empty database saved)
    void LoadItems(GenerationSettings settings)
    {
        var itemsById = _itemsById;
        itemsById.Clear();
        int missingIds = 0;
        foreach (var item in _sources.Keys)
        {
            if (string.IsNullOrEmpty(item.Id)) { missingIds++; continue; }
            if (!itemsById.TryAdd(item.Id, item))
                Debug.LogWarning($"AccountManager: {item.name} and {itemsById[item.Id].name} have the same Id. Run Tools → Assign Item IDs.", this);
        }
        if (missingIds > 0)
            Debug.LogWarning($"AccountManager: {missingIds} items have no Id, so they can't be loaded or saved. Run Tools → Assign Item IDs.", this);

        if (!AccountSaves.HasItemDatabase(AccountId))
        {
            AccountSaves.WriteItemDatabase(AccountId, _items);
            return;
        }
        _loadingItems = true;
        try
        {
            int loaded = AccountSaves.ReadItemDatabase(AccountId, _items, itemsById, settings);
            if (loaded >= 0) Debug.Log($"AccountManager: loaded {loaded} items for account '{AccountName}'.", this);
        }
        finally
        {
            _loadingItems = false;
        }
    }

    void OnItemChanged(ItemKey key)
    {
        if (!_loadingItems && _itemsInitialized) AccountSaves.WriteItemDatabase(AccountId, _items);
    }

    // Walks nested loot tables; each table once, so cycles can't loop forever
    void CollectItems(LootTable table, HashSet<LootTable> visited)
    {
        if (table == null || !visited.Add(table) || table.Entries == null) return;
        string tableName = string.IsNullOrWhiteSpace(table.Name) ? table.name : table.Name;
        foreach (var entry in table.Entries)
        {
            if (entry?.Entry is RollableItem item)
            {
                if (!_sources.TryGetValue(item, out var names)) _sources[item] = names = new List<string>();
                if (!names.Contains(tableName)) names.Add(tableName);
            }
            else if (entry?.Entry is LootTable nested) CollectItems(nested, visited);
        }
    }

    /// <summary>The database's item kind with this RollableItem.Id, or null.</summary>
    public RollableItem FindBaseItem(string id)
    {
        if (!_itemsInitialized) InitializeItems();
        return !string.IsNullOrEmpty(id) && _itemsById.TryGetValue(id, out var item) ? item : null;
    }

    /// <summary>Where this item kind can drop: the names of the loot tables that contain it. Later also enemies.</summary>
    public IReadOnlyList<string> SourcesOf(RollableItem item)
    {
        if (!_itemsInitialized) InitializeItems();
        return item != null && _sources.TryGetValue(item, out var names) ? names : Array.Empty<string>();
    }

    // ---------- Drops ----------

    /// <summary>
    /// A rolled item dropped. If its entry is empty it's stored right away (the popup just shows it); otherwise it waits
    /// in PendingDrops until the player takes it or keeps the current one (ResolveDrop).
    /// </summary>
    public void ReceiveDrop(Item item)
    {
        if (item == null || item.BaseItem == null) return;
        var key = ItemKey.Of(item);
        if (!Items.Contains(key))
        {
            Debug.LogWarning($"AccountManager: dropped {key} has no database entry, ignoring it.", this);
            return;
        }

        var kind = Items.Get(key) == null ? DropKind.NewItem : DropKind.Compare;
        if (kind == DropKind.NewItem) Items.Store(item);
        _pendingDrops.Add(new PendingDrop { Item = item, Kind = kind });
        PendingDropsChanged?.Invoke();
    }

    /// <summary>Resolves the oldest pending drop. takeNew replaces the database item (Compare drops only).</summary>
    public void ResolveDrop(bool takeNew)
    {
        if (_pendingDrops.Count == 0) return;
        var drop = _pendingDrops[0];
        _pendingDrops.RemoveAt(0);
        if (takeNew && drop.Kind == DropKind.Compare) Items.Store(drop.Item);
        PendingDropsChanged?.Invoke();
    }
}
