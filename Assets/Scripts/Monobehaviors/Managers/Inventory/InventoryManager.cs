using Equipment;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class InventoryManager : MonoBehaviour
{
    [Serializable]
    public class EquippedEntry
    {
        public string Slot;
        public string Item;
        public Rarity Rarity;
    }

    public static InventoryManager Instance;
    public InventorySlot WeaponSlot, HeadSlot, BootsSlot, LegsSlot, ChestSlot, BeltSlot,
                         HandsSlot, ShouldersSlot, AmuletSlot, Ring1Slot, Ring2Slot,
                         OffhandSlot;

    [Header("Summary (read-only, rebuilt whenever a slot changes)")]
    public StatSummary Stats = new StatSummary();
    public List<EquippedEntry> Equipped = new List<EquippedEntry>();

    /// <summary>Raised after the summary has been rebuilt.</summary>
    public event Action<StatSummary> StatsChanged;

    (string Name, InventorySlot Slot)[] NamedSlots => new[]
    {
        (nameof(WeaponSlot), WeaponSlot), (nameof(HeadSlot), HeadSlot), (nameof(BootsSlot), BootsSlot),
        (nameof(LegsSlot), LegsSlot), (nameof(ChestSlot), ChestSlot), (nameof(BeltSlot), BeltSlot),
        (nameof(HandsSlot), HandsSlot), (nameof(ShouldersSlot), ShouldersSlot), (nameof(AmuletSlot), AmuletSlot),
        (nameof(Ring1Slot), Ring1Slot), (nameof(Ring2Slot), Ring2Slot), (nameof(OffhandSlot), OffhandSlot),
    };

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        foreach (var (_, slot) in NamedSlots)
            if (slot != null) slot.Changed += OnSlotChanged;
    }

    void Start() => RecalculateStats();

    void OnDestroy()
    {
        foreach (var (_, slot) in NamedSlots)
            if (slot != null) slot.Changed -= OnSlotChanged;
        if (Instance == this) Instance = null;
    }

    void OnSlotChanged(InventorySlot slot) => RecalculateStats();

    /// <summary>Rebuilds Stats and Equipped from the current slots.</summary>
    [ContextMenu("Recalculate")]
    public void RecalculateStats()
    {
        Stats.Clear();
        Equipped.Clear();

        foreach (var (name, slot) in NamedSlots)
        {
            if (slot == null || slot.IsEmpty) continue;
            Stats.AddItem(slot.EquippedItem);
            Equipped.Add(new EquippedEntry
            {
                Slot = name,
                Item = slot.EquippedItem.BaseItem.Name,
                Rarity = slot.EquippedItem.Rarity
            });
        }

        Stats.Sort();
        StatsChanged?.Invoke(Stats);
    }

    [ContextMenu("Log Summary")]
    void LogSummary()
    {
        var sb = new StringBuilder("Equipped:\n");
        foreach (var e in Equipped) sb.AppendLine($"  {e.Slot}: {e.Item} ({e.Rarity})");
        sb.AppendLine("Totals:");
        sb.Append(Stats);
        Debug.Log(sb.ToString());
    }
}
