using System;
using System.Collections.Generic;
using Loottables;
using UnityEngine;

namespace Equipment
{
    public abstract class RollableItem : Lootable
    {
        // Legendary and Unique only come from their own item types via ForcedRarity
        static readonly Rarity[] DroppableRarities = { Rarity.Normal, Rarity.Magic, Rarity.Rare };

        public string Name;
        public Sprite Icon;                     // Shown in inventory/loot UI and editor tooling
        public abstract ItemType ItemType { get; }

        /// <summary>When set, drops of this base item always roll this rarity instead of a random one.</summary>
        public virtual Rarity? ForcedRarity => null;

        /// <summary>The rarities this base item can roll: its forced rarity, or Normal, Magic and Rare.</summary>
        public IReadOnlyList<Rarity> PossibleRarities => ForcedRarity is { } forced ? new[] { forced } : DroppableRarities;
        public List<Rollable> BaseAffixes;      // Always a part of the item
        public List<Rollable> RollableAffixes;  // 0 - x dependent on rarity

        /// <summary>Slot-specific base stats (weapon damage, armor, ...). Rolled before BaseAffixes.</summary>
        protected virtual IEnumerable<(Affix Affix, RollRange Range)> IntrinsicAffixes
        {
            get { yield break; }
        }

        /// <summary>
        /// Every roll range on this base item with where it comes from: the slot's intrinsic stats (weapon damage,
        /// armor, ...), then BaseAffixes, then RollableAffixes. Read-only; for tooling such as the Item Editor.
        /// </summary>
        public IEnumerable<(Affix Affix, RollRange Range, AffixSource Source)> AllRanges
        {
            get
            {
                foreach (var (affix, range) in IntrinsicAffixes)
                    if (range != null) yield return (affix, range, AffixSource.Intrinsic);
                if (BaseAffixes != null)
                    foreach (var rollable in BaseAffixes)
                        if (rollable?.RollRange != null) yield return (rollable.Affix, rollable.RollRange, AffixSource.Base);
                if (RollableAffixes != null)
                    foreach (var rollable in RollableAffixes)
                        if (rollable?.RollRange != null) yield return (rollable.Affix, rollable.RollRange, AffixSource.Rollable);
            }
        }

        /// <summary>Fixed (not rolled) stats this base item gives while equipped, e.g. a weapon's reach.</summary>
        public virtual void AddFixedStats(ref StatBlock stats) { }

        /// <summary>The fixed stats from AddFixedStats as readable lines, for tooltips.</summary>
        public virtual IEnumerable<string> FixedStatLines
        {
            get { yield break; }
        }

        public override List<Loot> Explore()
        {
            return new List<Loot> { CreateItem() };
        }

        /// <summary>Random rarity for a standard drop: Normal, Magic or Rare with equal odds.</summary>
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

        /// <summary>
        /// The range an affix on this base item can roll at the given rarity (the asset's RollRange narrowed by the
        /// rarity window, same as Roll). Base affixes are looked up in the intrinsic stats and BaseAffixes, rollable
        /// ones in RollableAffixes. Without settings the full asset range is returned.
        /// </summary>
        public bool TryGetRollRange(Affix affix, bool isBaseAffix, Rarity rarity, GenerationSettings settings, out float min, out float max)
        {
            min = max = 0f;
            var range = FindRange(affix, isBaseAffix);
            if (range == null) return false;

            var (minLerp, maxLerp) = settings == null ? (0f, 1f)
                : isBaseAffix ? settings.GetBaseRange(rarity)
                : settings.GetAffixRange(rarity);
            min = Mathf.Lerp(range.Min, range.Max, minLerp);
            max = Mathf.Lerp(range.Min, range.Max, maxLerp);
            return true;
        }

        RollRange FindRange(Affix affix, bool isBaseAffix)
        {
            if (isBaseAffix)
            {
                foreach (var (intrinsicAffix, range) in IntrinsicAffixes)
                    if (intrinsicAffix == affix) return range;
                return FindIn(BaseAffixes, affix);
            }
            return FindIn(RollableAffixes, affix);
        }

        static RollRange FindIn(List<Rollable> rollables, Affix affix)
        {
            if (rollables == null) return null;
            foreach (var rollable in rollables)
                if (rollable.Affix == affix) return rollable.RollRange;
            return null;
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
