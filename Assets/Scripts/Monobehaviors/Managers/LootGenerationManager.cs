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

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    [ContextMenu("Roll And Equip All")] // right-click the component in Play mode to test
    public void RollAndEquipAll()
    {
        var inv = InventoryManager.Instance;
        if (inv == null) { Debug.LogError("No InventoryManager in the scene."); return; }

        EquipFrom(inv.WeaponSlot, AllWeaponsLootTable);
        EquipFrom(inv.HeadSlot, AllHeadsLootTable);
        EquipFrom(inv.BootsSlot, AllBootsLootTable);
        EquipFrom(inv.LegsSlot, AllLegsLootTable);
        EquipFrom(inv.ChestSlot, AllChestLootTable);
        EquipFrom(inv.BeltSlot, AllBeltLootTable);
        EquipFrom(inv.HandsSlot, AllHandsLootTable);
        EquipFrom(inv.ShouldersSlot, AllShouldersLootTable);
        EquipFrom(inv.AmuletSlot, AllAmuletLootTable);
        EquipFrom(inv.Ring1Slot, AllRingLootTable);   // explored separately,
        EquipFrom(inv.Ring2Slot, AllRingLootTable);   // so each ring gets its own roll
        EquipFrom(inv.OffhandSlot, AllOffhandLootTable);
    }

    void EquipFrom(InventorySlot slot, LootTable table)
    {
        if (slot == null || table == null)
        {
            Debug.LogWarning("Missing slot or loot table reference, skipping.");
            return;
        }

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
            rolled.Rarity = RollableItem.GetRandomRarity();
            rolled.BaseItem.Roll(ref rolled);
            return rolled;
        }
        return null; // e.g. an And-mode table where nothing dropped
    }
}