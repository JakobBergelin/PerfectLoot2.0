using System;
using System.Collections.Generic;

namespace Equipment
{
    /// <summary>
    /// A rolled item. Its rarity and affixes come from BaseItem, Seed and RollVersion (RollableItem.RollFromSeed), so
    /// the same three always give the same item; Guid tells this one item apart from every other.
    /// </summary>
    [Serializable]
    public class Item : Loottables.Loot
    {
        public string Guid;
        public uint Seed;
        public int RollVersion;
        [UnityEngine.Tooltip("Failed a check when loaded (ItemValidation, a changed rarity or a shared Guid); stays marked")]
        public bool Unverified;
        public RollableItem BaseItem;
        public Rarity Rarity;
        public List<Rolled> BaseAffixes;
        public List<Rolled> RollableAffixes;
        public ItemType ItemType => BaseItem.ItemType;
    }
}
