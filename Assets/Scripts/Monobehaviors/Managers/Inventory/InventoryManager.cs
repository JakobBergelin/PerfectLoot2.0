using Equipment;
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;
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
        _equipmentStatsDirty = true;
        TryPushEquipmentStats();
        StatsChanged?.Invoke(Stats);
    }

    bool _equipmentStatsDirty;

    // The player entity may not exist yet when slots change (subscene still loading), so retry until it does.
    void Update()
    {
        if (_equipmentStatsDirty) TryPushEquipmentStats();
    }

    /// <summary>Writes the summed equipment stats to the player's EquipmentStats component.</summary>
    void TryPushEquipmentStats()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;

        var entityManager = world.EntityManager;
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>(), ComponentType.ReadWrite<EquipmentStats>());
        if (query.IsEmpty) return;

        // Rolled affixes plus fixed per-item stats (weapon reach, weapon damage range)
        var block = Stats.ToStatBlock();
        foreach (var (_, slot) in NamedSlots)
            if (slot != null && !slot.IsEmpty) slot.EquippedItem.BaseItem.AddFixedStats(ref block);

        var equipmentStats = new EquipmentStats { Value = block };
        using var players = query.ToEntityArray(Allocator.Temp);
        foreach (var player in players)
            entityManager.SetComponentData(player, equipmentStats);

        _equipmentStatsDirty = false;
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
