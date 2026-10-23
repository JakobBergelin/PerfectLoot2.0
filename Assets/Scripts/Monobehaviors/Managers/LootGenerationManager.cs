using System.Linq;
using Equipment;
using Loottables;
using UnityEngine;

public class LootGenerationManager : MonoBehaviour
{
    public static LootGenerationManager Instance;
    public GenerationSettings GenerationSettings;

    public LootTable AllWeaponsLootTable;
    public LootTable AllHeadsLootTable;
    public LootTable AllBootsLootTable;
    public LootTable AllLegsLootTable;
    public LootTable AllChestLootTable;
    public LootTable AllBeltLootTable;
    public LootTable AllHandsLootTable;
    public LootTable AllShouldersLootTable;
    public LootTable AllAmuletLootTable;
    public LootTable AllRingLootTable;
    public LootTable AllOffhandLootTable;

    [Header("Testing")]
    [Tooltip("At game start, roll a Standard item into the account database for every empty inventory slot and equip it.")]
    public bool RollAndEquipOnStart = true;

    // Rerolls per slot when the table gives a Legendary/Unique, an already equipped key, or nothing
    const int MaxRollAttempts = 50;

    public LootTable[] AllLootTables => new[]
    {
        AllWeaponsLootTable, AllHeadsLootTable, AllBootsLootTable, AllLegsLootTable, AllChestLootTable, AllBeltLootTable,
        AllHandsLootTable, AllShouldersLootTable, AllAmuletLootTable, AllRingLootTable, AllOffhandLootTable,
    };

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // InventoryManager subscribes to its slots in Awake, so equips here reach EquipmentStats
    void Start()
    {
        if (RollAndEquipOnStart) RollAndEquip(onlyIfEmpty: true);
    }

    [ContextMenu("Roll And Equip All")] // right-click the component in Play mode to test
    public void RollAndEquipAll() => RollAndEquip(onlyIfEmpty: false);

    void RollAndEquip(bool onlyIfEmpty)
    {
        var inv = InventoryManager.Instance;
        if (inv == null) { Debug.LogError("No InventoryManager in the scene."); return; }

        EquipFrom(inv.WeaponSlot, AllWeaponsLootTable, onlyIfEmpty);
        EquipFrom(inv.HeadSlot, AllHeadsLootTable, onlyIfEmpty);
        EquipFrom(inv.BootsSlot, AllBootsLootTable, onlyIfEmpty);
        EquipFrom(inv.LegsSlot, AllLegsLootTable, onlyIfEmpty);
        EquipFrom(inv.ChestSlot, AllChestLootTable, onlyIfEmpty);
        EquipFrom(inv.BeltSlot, AllBeltLootTable, onlyIfEmpty);
        EquipFrom(inv.HandsSlot, AllHandsLootTable, onlyIfEmpty);
        EquipFrom(inv.ShouldersSlot, AllShouldersLootTable, onlyIfEmpty);
        EquipFrom(inv.AmuletSlot, AllAmuletLootTable, onlyIfEmpty);
        EquipFrom(inv.Ring1Slot, AllRingLootTable, onlyIfEmpty);   // explored separately,
        EquipFrom(inv.Ring2Slot, AllRingLootTable, onlyIfEmpty);   // so each ring gets its own roll
        EquipFrom(inv.OffhandSlot, AllOffhandLootTable, onlyIfEmpty);
    }

    // Test data: rolls a Standard item whose key isn't equipped in another slot, stores it in the account database
    // (replacing what was there) and equips it
    void EquipFrom(InventorySlot slot, LootTable table, bool onlyIfEmpty)
    {
        if (slot == null || table == null)
        {
            Debug.LogWarning("Missing slot or loot table reference, skipping.");
            return;
        }
        if (onlyIfEmpty && !slot.IsEmpty) return;

        var inventory = InventoryManager.Instance;
        var database = AccountManager.Instance != null ? AccountManager.Instance.Items : null;
        if (database == null) { Debug.LogError("No AccountManager in the scene."); return; }

        for (int attempt = 0; attempt < MaxRollAttempts; attempt++)
        {
            var item = RollItem(table, slot.SlotType);
            if (item == null || item.BaseItem.ForcedRarity != null) continue;
            var key = ItemKey.Of(item);
            var holder = inventory.SlotWith(key);
            if (holder != null && holder != slot) continue;

            if (database.Store(item) && inventory.Equip(slot, key)) return;
        }
        Debug.LogWarning($"Couldn't roll a Standard {slot.SlotType} for {slot.name} from {table.name}.");
    }

    public Item RollItem(LootTable table, ItemType expectedType)
    {
        foreach (var candidate in table.Explore().OfType<Item>())
        {
            if (candidate.ItemType != expectedType)
            {
                Debug.LogWarning($"{table.name} produced a {candidate.ItemType}, expected {expectedType}.");
                continue;
            }

            return RollItem(candidate.BaseItem);
        }
        return null; // e.g. an And-mode table where nothing dropped
    }

    /// <summary>A new item of this base, rolled at its forced rarity (Legendary/Unique) or a random one.</summary>
    public Item RollItem(RollableItem baseItem)
    {
        var item = baseItem.CreateItem();
        item.Rarity = baseItem.ForcedRarity ?? RollableItem.GetRandomRarity();
        baseItem.Roll(ref item);
        return item;
    }
}