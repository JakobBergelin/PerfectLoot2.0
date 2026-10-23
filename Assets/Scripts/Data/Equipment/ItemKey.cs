using System;

namespace Equipment
{
    /// <summary>An item kind (base item asset) and rarity: one ItemDatabase entry, and what inventory slots point to.</summary>
    [Serializable]
    public struct ItemKey : IEquatable<ItemKey>
    {
        public RollableItem BaseItem;
        public Rarity Rarity;

        public ItemKey(RollableItem baseItem, Rarity rarity)
        {
            BaseItem = baseItem;
            Rarity = rarity;
        }

        public static ItemKey Of(Item item) => new ItemKey(item.BaseItem, item.Rarity);

        /// <summary>False for the default key (Unity serializes an unset key with no base item).</summary>
        public bool IsSet => BaseItem != null;

        public bool Equals(ItemKey other) => BaseItem == other.BaseItem && Rarity == other.Rarity;
        public override bool Equals(object obj) => obj is ItemKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(BaseItem != null ? BaseItem.GetHashCode() : 0, (int)Rarity);
        public static bool operator ==(ItemKey a, ItemKey b) => a.Equals(b);
        public static bool operator !=(ItemKey a, ItemKey b) => !a.Equals(b);

        public override string ToString() => IsSet ? $"{Rarity} {BaseItem.Name}" : "None";
    }
}
