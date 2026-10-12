using Equipment;
using System;
using UnityEngine;

// InventoryManager.cs
public class InventoryManager : MonoBehaviour
{
    public static InventoryManager Instance;
    public InventorySlot WeaponSlot, HeadSlot, BootsSlot, LegsSlot, ChestSlot, BeltSlot,
                         HandsSlot, ShouldersSlot, AmuletSlot, Ring1Slot, Ring2Slot,
                         OffhandSlot; // new

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }
}