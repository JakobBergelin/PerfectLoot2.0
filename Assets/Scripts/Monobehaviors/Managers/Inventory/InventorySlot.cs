using Equipment;
using System;
using UnityEngine;

public class InventorySlot : MonoBehaviour
{
    public ItemType SlotType;
    [Tooltip("The account ItemDatabase entry this slot shows. Equip through InventoryManager.Equip.")]
    public ItemKey EquippedKey;

    /// <summary>Raised after the equipped key was set or removed.</summary>
    public event Action<InventorySlot> Changed;

    /// <summary>The item stored in the account database for EquippedKey, or null.</summary>
    public Item EquippedItem => EquippedKey.IsSet && AccountManager.Instance != null ? AccountManager.Instance.Items.Get(EquippedKey) : null;

    /// <summary>True when the key is unset or the database has no item for it.</summary>
    public bool IsEmpty => EquippedItem == null;

    /// <summary>Sets the key without checks; InventoryManager.Equip validates first.</summary>
    public void SetKey(ItemKey key)
    {
        EquippedKey = key;
        Changed?.Invoke(this);
    }

    public void Clear() => SetKey(default);
}
