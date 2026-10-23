using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Equipment
{
    /// <summary>
    /// The account's found items: one entry per item kind and rarity (Normal, Magic and Rare for standard items, one
    /// for Legendary/Unique). An entry is empty until an item of that kind and rarity has been found. Characters'
    /// inventory slots point to entries by ItemKey, so replacing an entry's item updates every slot that uses it.
    /// Play mode only for now (not saved).
    /// </summary>
    [Serializable]
    public class ItemDatabase
    {
        [Serializable]
        public class Entry
        {
            public ItemKey Key;
            [Tooltip("Empty until an item of this kind and rarity has been found")]
            public Item Item;

            /// <summary>Unity serializes an unset Item as a blank object rather than null.</summary>
            public bool IsEmpty => Item == null || Item.BaseItem == null;
        }

        public List<Entry> Entries = new List<Entry>();

        /// <summary>Raised after an entry's item was stored or replaced.</summary>
        public event Action<ItemKey> ItemChanged;

        readonly Dictionary<ItemKey, Entry> _lookup = new Dictionary<ItemKey, Entry>();

        /// <summary>Creates an empty entry for every rarity of every given item kind, sorted by slot, name and rarity.</summary>
        public void Initialize(IEnumerable<RollableItem> kinds)
        {
            Entries.Clear();
            _lookup.Clear();
            foreach (var kind in kinds.Where(kind => kind != null).Distinct()
                                      .OrderBy(kind => kind.ItemType).ThenBy(kind => kind.Name))
            {
                foreach (var rarity in kind.PossibleRarities)
                {
                    var entry = new Entry { Key = new ItemKey(kind, rarity) };
                    Entries.Add(entry);
                    _lookup[entry.Key] = entry;
                }
            }
        }

        public bool Contains(ItemKey key) => _lookup.ContainsKey(key);

        /// <summary>The item stored for this key, or null when none has been found (or the key isn't in the database).</summary>
        public Item Get(ItemKey key) =>
            _lookup.TryGetValue(key, out var entry) && !entry.IsEmpty ? entry.Item : null;

        /// <summary>All keys for one slot, in database order.</summary>
        public IEnumerable<ItemKey> KeysFor(ItemType slot) =>
            Entries.Where(entry => entry.Key.BaseItem.ItemType == slot).Select(entry => entry.Key);

        /// <summary>Stores a rolled item in its entry, replacing the item there. Returns false for unknown kinds or rarities.</summary>
        public bool Store(Item item)
        {
            if (item == null || item.BaseItem == null) return false;
            var key = ItemKey.Of(item);
            if (!_lookup.TryGetValue(key, out var entry))
            {
                Debug.LogWarning($"ItemDatabase has no entry for {key} (not in a loot table, or not a rarity it can have).");
                return false;
            }

            entry.Item = item;
            ItemChanged?.Invoke(key);
            return true;
        }
    }
}
