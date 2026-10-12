// InventorySlot.cs (its own file, see issue 2)
using Equipment;
using System;
using UnityEngine;

public class InventorySlot : MonoBehaviour
{
    public ItemType SlotType;
    public Item EquippedItem;

    public bool EquipItem(Item item)
    {
        if (item.ItemType != SlotType)
        {
            Debug.LogWarning($"Can't put {item.ItemType} into a {SlotType} slot.");
            return false;
        }
        EquippedItem = item;
        return true;
    }

    public void UnequipItem() => EquippedItem = null;
}