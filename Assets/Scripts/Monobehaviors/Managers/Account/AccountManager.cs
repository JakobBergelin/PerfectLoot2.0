using System;
using System.Collections.Generic;
using Equipment;
using Loottables;
using UnityEngine;

/// <summary>
/// Account-wide state that outlives scenes: the item database shared by all characters (later also the characters
/// themselves), and the queue of dropped items waiting for the player (DropPopupHUD). Play mode only for now.
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

    [SerializeField, Tooltip("Read-only: one entry per item kind and rarity, empty until found")]
    ItemDatabase _items = new ItemDatabase();
    bool _itemsInitialized;

    // Item kind -> names of the loot tables that contain it
    readonly Dictionary<RollableItem, List<string>> _sources = new Dictionary<RollableItem, List<string>>();

    readonly List<PendingDrop> _pendingDrops = new List<PendingDrop>();

    /// <summary>Dropped items waiting for the player, oldest first.</summary>
    public IReadOnlyList<PendingDrop> PendingDrops => _pendingDrops;

    /// <summary>Raised after a drop was queued or resolved.</summary>
    public event Action PendingDropsChanged;

    /// <summary>
    /// The account's item database. Built on first use from every item in LootGenerationManager's loot tables (items
    /// that can't drop get no entries), so it's ready no matter which manager's Start runs first.
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
        _itemsInitialized = true;
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
