using System.Collections.Generic;
using Equipment;

/// <summary>
/// A character on an account: what the player chose when creating it, and its progress. Saved per account by
/// AccountSaves; the item database is shared by all of the account's characters.
/// </summary>
public class AccountCharacter
{
    /// <summary>An item equipped in one of the character's inventory slots: a database entry (base item and rarity).</summary>
    public class EquippedItem
    {
        public string Slot;         // InventoryManager's slot name, e.g. WeaponSlot or Ring2Slot
        public string BaseItemId;   // RollableItem.Id
        public Rarity Rarity;
    }

    public string Guid;
    public string Name;
    public ClassType Class;
    public ResourceType Resource;
    public int Level = 1;
    public List<EquippedItem> Equipped = new List<EquippedItem>();
}
