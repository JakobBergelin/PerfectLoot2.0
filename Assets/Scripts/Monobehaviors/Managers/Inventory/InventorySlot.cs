using Equipment;
using System;
using UnityEngine;

public class InventorySlot : MonoBehaviour
{
    public ItemType SlotType;
    public Item EquippedItem;

    /// <summary>Raised after the equipped item was set or removed.</summary>
    public event Action<InventorySlot> Changed;

    /// <summary>True when nothing is equipped. Unity serializes EquippedItem as a blank object rather than null.</summary>
    public bool IsEmpty => EquippedItem == null || EquippedItem.BaseItem == null;

    public bool EquipItem(Item item)
    {
        if (item == null) return false;
        if (item.ItemType != SlotType)
        {
            Debug.LogWarning($"Can't put {item.ItemType} into a {SlotType} slot.");
            return false;
        }
        EquippedItem = item;
        Changed?.Invoke(this);
        return true;
    }

    public void UnequipItem()
    {
        EquippedItem = null;
        Changed?.Invoke(this);
    }
}
