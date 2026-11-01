using System.Collections.Generic;
using Equipment;

/// <summary>
/// The player's stats as they would be with one inventory slot holding another item (or nothing), for the character
/// panel's swap picker. Takes the equipment part out of the finalized stats, puts the changed one in and redoes
/// FinalizeStatsSystem's math. Legendary powers and passives that react to stats aren't simulated.
/// </summary>
public static class SwapPreview
{
    /// <summary>The equipped items, with slot holding item instead of its own (null: empty).</summary>
    public static IEnumerable<Item> ItemsWith(InventoryManager inventory, InventorySlot slot, Item item)
    {
        foreach (var other in inventory.Slots)
        {
            if (other == null) continue;
            var equipped = other == slot ? item : other.EquippedItem;
            if (equipped != null && equipped.BaseItem != null) yield return equipped;
        }
    }

    /// <summary>Summed stats of these items, as InventoryManager writes them to EquipmentStats.</summary>
    public static StatBlock EquipmentStats(IEnumerable<Item> items)
    {
        var summary = new StatSummary();
        var list = new List<Item>(items);
        foreach (var item in list) summary.AddItem(item);
        var block = summary.ToStatBlock();
        foreach (var item in list) item.BaseItem.AddFixedStats(ref block);
        return block;
    }

    /// <summary>The legendary powers these items give, each once.</summary>
    public static List<LegendaryPower> Powers(IEnumerable<Item> items)
    {
        var powers = new List<LegendaryPower>();
        foreach (var item in items)
            if (item.BaseItem is ILegendaryItem legendary && legendary.Power != null && !powers.Contains(legendary.Power))
                powers.Add(legendary.Power);
        return powers;
    }

    /// <summary>
    /// Finalized stats with the equipment part changed from current to changed. Only weapons give reach and cooldown,
    /// so when current has none the finalized values are FinalizeStatsSystem's fallbacks and are undone first.
    /// </summary>
    public static StatBlock Swap(StatBlock finalized, in StatBlock current, in StatBlock changed, float unarmedReach)
    {
        var stats = finalized;
        stats.AttackSpeed *= stats.WeaponCooldown;
        if (current.WeaponCooldown <= 0f) stats.WeaponCooldown = 0f;
        if (current.WeaponReach <= 0f) stats.WeaponReach = 0f;

        stats.Subtract(current);
        stats.Add(changed);

        // Same math as FinalizeStatsSystem
        if (stats.WeaponReach <= 0f) stats.WeaponReach = unarmedReach;
        if (stats.WeaponCooldown <= 0f) stats.WeaponCooldown = 1f;
        stats.AttackSpeed /= stats.WeaponCooldown;
        return stats;
    }
}
