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

    /// <summary>A dungeon the character has run: how often it finished it, and its difficulties (DungeonDifficulty).</summary>
    public class CompletedDungeon
    {
        public string DungeonId;    // DungeonDefinition.Id
        public int Count;           // Finished
        public int Highest;         // The highest difficulty finished (0: none); Highest + 1 is unlocked
        public int Last = 1;        // The difficulty of its last run
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
    // Easing into skills (ActiveSkillManager): the class's SecondSkill was given, and the SkillType names the player has
    // seen in the Skills panel since they became usable (the rest show "New" there)
    public bool GotSecondSkill;
    public List<string> SeenSkills = new List<string>();
    public List<CompletedDungeon> CompletedDungeons = new List<CompletedDungeon>();
    // Made but never in a town yet: the game starts it in the first dungeon (GameBootstrap.StartScene) until it gets there.
    // False for characters saved before this existed, so they keep starting in town
    public bool NewToTown;
}
