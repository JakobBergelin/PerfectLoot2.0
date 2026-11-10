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

    /// <summary>How often the character has finished a dungeon.</summary>
    public class CompletedDungeon
    {
        public string DungeonId;    // DungeonDefinition.Id
        public int Count;
    }

    public string Guid;
    public string Name;
    public ClassType Class;
    public ResourceType Resource;
    public int Level = 1;
    public int Experience;      // Toward the next level
    public List<EquippedItem> Equipped = new List<EquippedItem>();
    // The equipped skills by slot as SkillType / PassiveType names, "" for an empty slot. Empty lists until the character
    // was first played (ActiveSkillManager and PassiveSkillManager fill and save them then).
    public List<string> ActiveSkills = new List<string>();
    public List<string> Passives = new List<string>();
    public List<CompletedDungeon> CompletedDungeons = new List<CompletedDungeon>();
}
