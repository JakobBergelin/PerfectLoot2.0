using System;
using System.Collections.Generic;

namespace Equipment
{
    [Serializable]
    public class Item : Loottables.Loot
    {
        public RollableItem BaseItem;
        public Rarity Rarity;
        public List<Rolled> BaseAffixes;
        public List<Rolled> RollableAffixes;
        public ItemType ItemType => BaseItem.ItemType;
    }
}
