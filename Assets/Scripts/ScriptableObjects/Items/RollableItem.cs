using System;
using System.Collections.Generic;
using Loottables;
using UnityEngine;

namespace Equipment
{
    public abstract class RollableItem : Lootable
    {
        static readonly Rarity[] DroppableRarities = Array.FindAll((Rarity[])Enum.GetValues(typeof(Rarity)), r => r != Rarity.Unrolled);

        public string Name;
        public abstract ItemType ItemType { get; }
        public List<Rollable> BaseAffixes;      // Always a part of the item
        public List<Rollable> RollableAffixes;  // 0 - x dependent on rarity

        /// <summary>Slot-specific base stats (weapon damage, armor, ...). Rolled before BaseAffixes.</summary>
        protected virtual IEnumerable<(Affix Affix, RollRange Range)> IntrinsicAffixes
        {
            get { yield break; }
        }

        public override List<Loot> Explore()
        {
            return new List<Loot> { CreateItem() };
        }

        /// <summary>Random rarity for a drop. Never returns Unrolled.</summary>
        public static Rarity GetRandomRarity()
        {
            return DroppableRarities[UnityEngine.Random.Range(0, DroppableRarities.Length)];
        }

        public Item CreateItem()
        {
            return new Item
            {
                Rarity = Rarity.Unrolled, //TODO
                Quantity = 1,
                BaseItem = this,
                BaseAffixes = new List<Rolled>(),
                RollableAffixes = new List<Rolled>()
            };
        }

        public void Roll(ref Item item)
        {
            if (item.Rarity == Rarity.Unrolled) return;

            var settings = LootGenerationManager.Instance.GenerationSettings;
            RollBaseAffixes(item, settings);
            RollRollableAffixes(item, settings);
        }

        void RollBaseAffixes(Item item, GenerationSettings settings)
        {
            var (min, max) = settings.GetBaseRange(item.Rarity);

            foreach (var (affix, range) in IntrinsicAffixes)
                item.BaseAffixes.Add(new Rolled { Affix = affix, Value = range.Random(min, max) });

            foreach (var baseAffix in BaseAffixes)
                item.BaseAffixes.Add(new Rolled { Affix = baseAffix.Affix, Value = baseAffix.RollRange.Random(min, max) });
        }

        void RollRollableAffixes(Item item, GenerationSettings settings)
        {
            int count = item.Rarity switch
            {
                Rarity.Unique => RollableAffixes.Count,
                Rarity.Magic => 1,
                Rarity.Rare => 3,
                Rarity.Legendary => 2,
                _ => 0
            };
            count = Mathf.Min(count, RollableAffixes.Count);
            if (count == 0) return;

            var (min, max) = settings.GetAffixRange(item.Rarity);
            var pool = new List<Rollable>(RollableAffixes);

            for (int i = 0; i < count; i++)
            {
                // Partial shuffle so the same affix is never picked twice (skipped when taking all).
                if (count < pool.Count)
                {
                    int j = UnityEngine.Random.Range(i, pool.Count);
                    (pool[i], pool[j]) = (pool[j], pool[i]);
                }
                item.RollableAffixes.Add(new Rolled { Affix = pool[i].Affix, Value = pool[i].RollRange.Random(min, max) });
            }
        }
    }
}
