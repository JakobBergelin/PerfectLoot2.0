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
    [Tooltip("At game start, roll and equip an item into every empty inventory slot.")]
    public bool RollAndEquipOnStart = true;

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

    void EquipFrom(InventorySlot slot, LootTable table, bool onlyIfEmpty)
    {
        if (slot == null || table == null)
        {
            Debug.LogWarning("Missing slot or loot table reference, skipping.");
            return;
        }
        if (onlyIfEmpty && !slot.IsEmpty) return;

        var item = RollItem(table, slot.SlotType);
        if (item != null) slot.EquipItem(item);
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

            Item rolled = candidate; // a foreach variable can't be passed by ref
            rolled.Rarity = rolled.BaseItem.ForcedRarity ?? RollableItem.GetRandomRarity();
            rolled.BaseItem.Roll(ref rolled);
            return rolled;
        }
        return null; // e.g. an And-mode table where nothing dropped
    }
}