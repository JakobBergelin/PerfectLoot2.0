using System.Collections.Generic;
using Equipment;
using Loottables;
using UnityEngine;

/// <summary>
/// Account-wide state that outlives scenes: the item database shared by all characters (later also the characters
/// themselves). Play mode only for now.
/// </summary>
public class AccountManager : MonoBehaviour
{
    public static AccountManager Instance;

    [SerializeField, Tooltip("Read-only: one entry per item kind and rarity, empty until found")]
    ItemDatabase _items = new ItemDatabase();
    bool _itemsInitialized;

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

        var kinds = new List<RollableItem>();
        var visited = new HashSet<LootTable>();
        foreach (var table in loot.AllLootTables)
            CollectItems(table, kinds, visited);

        _items.Initialize(kinds);
        _itemsInitialized = true;
    }

    // Walks nested loot tables; each table once, so cycles can't loop forever
    static void CollectItems(LootTable table, List<RollableItem> items, HashSet<LootTable> visited)
    {
        if (table == null || !visited.Add(table) || table.Entries == null) return;
        foreach (var entry in table.Entries)
        {
            if (entry?.Entry is RollableItem item) items.Add(item);
            else if (entry?.Entry is LootTable nested) CollectItems(nested, items, visited);
        }
    }
}
