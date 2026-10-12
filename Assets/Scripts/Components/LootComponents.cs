

//using Loottables;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Xml.Linq;
//using Unity.Mathematics;
//using UnityEngine;

//namespace Equipment
//{
//    public enum Affix
//    {
//        Strength,
//        Agility,
//        Intelligence,
//        MovementSpeed,
//        AllResistance,
//        BlockChance,
//        CooldownReduction,
//        ResourceCostReduction,
//        DodgeChance,
//        AttackSpeed,
//        WeaponDamage,
//        WeaponDamageRange,
//        Lifesteal,
//        ProjectilePierces, // int?
//        ProjectileBounces,// int?
//        Damage,
//        AreaOfEffect,
//        ColdResistance,
//        FireResistance,
//        LightningResistance,
//        CriticalChance,
//        CriticalDamage,
//        Armor,
//        ResourceSteal,
//        Health,
//        HealthRegeneration,
//        Resource,
//        ResourceRegeneration,

//    }

//    public enum ItemType
//    {
//        Weapon,
//        Head,
//        Chest,
//        Legs,
//        Belt,
//        Boots,
//        Hands,
//        Shoulders,
//        Amulet,
//        Ring,
//        Offhand,
//    }


//    public enum LegendaryAffix
//    {
//        None,
//        BrokenPromises,
//        Vanguard,
//    }

//    [Serializable]
//    public enum Rarity
//    {
//        Unrolled, 
//        Normal,
//        Magic,
//        Rare,
//        Legendary,
//        Unique,
//    }

//    [Serializable]
//    public class RollRange
//    {
//        public float Min;
//        public float Max;

//        public float Random(float minLerp, float maxLerp)
//        {
//            return UnityEngine.Random.Range(Mathf.Lerp(Min, Max, minLerp), Mathf.Lerp(Min, Max, maxLerp));
//        }
//    }

//    [Serializable]
//    public class Rollable
//    {
//        public Affix Affix;
//        public RollRange RollRange;
//    }

//    [Serializable]
//    public class Rolled
//    {
//        public Affix Affix;
//        public float Value;
//    }

//    public abstract class RollableItem : Loottables.Lootable
//    {
//        public string Name;
//        public abstract ItemType ItemType { get; }
//        public List<Rollable> BaseAffixes; //Always as a part of the item
        
//        public List<Rollable> RollableAffixes; // 0 - x dependent on rarity

//        public override List<Loot> Explore()
//        {
//            return new List<Loot> { this.CreateItem() };
//        }

//        public static Rarity GetRandomRarity()
//        {
//            var values = (Rarity[])Enum.GetValues(typeof(Rarity));
//            return values[UnityEngine.Random.Range(1, values.Length)]; // skip Unrolled
//        }

//        public Item CreateItem()
//        {
//            var item = new Item
//            {
//                Rarity = Rarity.Unrolled, //TODO 
//                Quantity = 1,
//                BaseItem = this,
//                BaseAffixes = new List<Rolled>(),
//                RollableAffixes = new List<Rolled>()
//            };
//            return item;
//        }

//        public virtual void Roll(ref Item item)
//        {
//            foreach (var baseAffix in BaseAffixes)
//            {
//                switch (item.Rarity)
//                {
//                    case Rarity.Unique:
//                    case Rarity.Normal:
//                        item.BaseAffixes.Add(new Rolled { Affix = baseAffix.Affix, Value = UnityEngine.Random.Range(baseAffix.RollRange.Min, baseAffix.RollRange.Max) });
//                        break;
//                    case Rarity.Magic:
//                        var magicValue = baseAffix.RollRange.Random(LootGenerationManager.Instance.GenerationSettings.MagicBaseMin, LootGenerationManager.Instance.GenerationSettings.MagicBaseMax);
//                        item.BaseAffixes.Add(new Rolled { Affix = baseAffix.Affix, Value = magicValue });
//                        break;
//                    case Rarity.Rare:
//                        var rareValue = baseAffix.RollRange.Random(LootGenerationManager.Instance.GenerationSettings.RareBaseMin, LootGenerationManager.Instance.GenerationSettings.RareBaseMax);
//                        item.BaseAffixes.Add(new Rolled { Affix = baseAffix.Affix, Value = rareValue });
//                        break;
//                    case Rarity.Legendary:
//                        var legendaryValue = baseAffix.RollRange.Random(LootGenerationManager.Instance.GenerationSettings.LegendaryBaseMin, LootGenerationManager.Instance.GenerationSettings.LegendaryBaseMax);
//                        item.BaseAffixes.Add(new Rolled { Affix = baseAffix.Affix, Value = legendaryValue });
//                        break;
//                }
//            }

//            switch (item.Rarity)
//            {
//                case Rarity.Unique:
//                    foreach (var rollablaffix in RollableAffixes)
//                    {
//                        item.RollableAffixes.Add(new Rolled { Affix = rollablaffix.Affix, Value = rollablaffix.RollRange.Random(0,1) });
//                    }
//                    break;
//                case Rarity.Magic:
//                    var randomAffix = RollableAffixes[UnityEngine.Random.Range(0, RollableAffixes.Count)];
//                    item.RollableAffixes.Add(new Rolled { Affix = randomAffix.Affix, Value = randomAffix.RollRange.Random(LootGenerationManager.Instance.GenerationSettings.MagicAffixMin, LootGenerationManager.Instance.GenerationSettings.MagicAffixMax) });
//                    break;
//                case Rarity.Rare:
//                    {
//                        var shuffled = new List<Rollable>(RollableAffixes);
//                        for (int i = 0; i < 3; i++)
//                        {
//                            int randomIndex = UnityEngine.Random.Range(i, RollableAffixes.Count);
//                            (shuffled[i], shuffled[randomIndex]) =
//                                (shuffled[randomIndex], shuffled[i]);
//                        }

//                        Rollable first = shuffled[0];
//                        item.RollableAffixes.Add(new Rolled { Affix = first.Affix, Value = first.RollRange.Random(LootGenerationManager.Instance.GenerationSettings.RareAffixMin, LootGenerationManager.Instance.GenerationSettings.RareAffixMax) });
//                        Rollable second = shuffled[1];
//                        item.RollableAffixes.Add(new Rolled { Affix = second.Affix, Value = second.RollRange.Random(LootGenerationManager.Instance.GenerationSettings.RareAffixMin, LootGenerationManager.Instance.GenerationSettings.RareAffixMax) });
//                        Rollable third = shuffled[2];
//                        item.RollableAffixes.Add(new Rolled { Affix = third.Affix, Value = third.RollRange.Random(LootGenerationManager.Instance.GenerationSettings.RareAffixMin, LootGenerationManager.Instance.GenerationSettings.RareAffixMax) });
//                    }

//                    break;
//                case Rarity.Legendary:
//                    {
//                        var shuffled = new List<Rollable>(RollableAffixes);
//                        for (int i = 0; i < 2; i++)
//                        {
//                            int randomIndex = UnityEngine.Random.Range(i, RollableAffixes.Count);
//                            (shuffled[i], shuffled[randomIndex]) =
//                                (shuffled[randomIndex], shuffled[i]);
//                        }

//                        Rollable first = shuffled[0];
//                        item.RollableAffixes.Add(new Rolled { Affix = first.Affix, Value = first.RollRange.Random(LootGenerationManager.Instance.GenerationSettings.LegendaryAffixMin, LootGenerationManager.Instance.GenerationSettings.LegendaryAffixMax) });
//                        Rollable second = shuffled[1];
//                        item.RollableAffixes.Add(new Rolled { Affix = second.Affix, Value = second.RollRange.Random(LootGenerationManager.Instance.GenerationSettings.LegendaryAffixMin, LootGenerationManager.Instance.GenerationSettings.LegendaryAffixMax) });
//                    }
//                    break;
//            }
//        }
//    }

//    [CreateAssetMenu(fileName = "RollableWeapon", menuName = "Items/Weapon")]
//    public class RollableWeaponItem : RollableItem
//    {
//        public override ItemType ItemType => ItemType.Weapon;
//        public RollRange WeaponDamage;
//        public float WeaponCooldown;
//        public float WeaponDamageRange;

//        public override void Roll(ref Item item)
//        {
//            switch(item.Rarity)
//            {
//                case Rarity.Unique:
//                case Rarity.Normal:
//                    item.BaseAffixes.Add(new Rolled { Affix = Affix.WeaponDamage, Value = WeaponDamage.Random(0,1) });
//                    break;
//                case Rarity.Magic:
//                    item.BaseAffixes.Add(new Rolled { Affix = Affix.WeaponDamage, Value = WeaponDamage.Random(LootGenerationManager.Instance.GenerationSettings.MagicBaseMin, LootGenerationManager.Instance.GenerationSettings.MagicBaseMax) });
//                    break;
//                case Rarity.Rare:
//                    item.BaseAffixes.Add(new Rolled { Affix = Affix.WeaponDamage, Value = WeaponDamage.Random(LootGenerationManager.Instance.GenerationSettings.RareBaseMin, LootGenerationManager.Instance.GenerationSettings.RareBaseMax) });
//                    break;
//                case Rarity.Legendary:
//                    item.BaseAffixes.Add(new Rolled { Affix = Affix.WeaponDamage, Value = WeaponDamage.Random(LootGenerationManager.Instance.GenerationSettings.LegendaryBaseMin, LootGenerationManager.Instance.GenerationSettings.LegendaryBaseMax) });
//                    break;
//            }

//            base.Roll(ref item);
//        }
//    }

//    [CreateAssetMenu(fileName = "RollableLegendaryWeapon", menuName = "Items/LegendaryWeapon")]
//    public class RollableLegendaryWeaponItem : RollableWeaponItem
//    {
//        public LegendaryAffix LegendaryAffix;

//    }

//    [CreateAssetMenu(fileName = "RollableHead", menuName = "Items/Head")]
//    public class RollableHeadItem : RollableItem
//    {
//        public override ItemType ItemType => ItemType.Head;
//    }

//    [CreateAssetMenu(fileName = "RollableLegendaryHead", menuName = "Items/LegendaryHead")]
//    public class RollableLegendaryHeadItem : RollableHeadItem
//    {
//        public LegendaryAffix LegendaryAffix;
//    }

//    // Boots
//    [CreateAssetMenu(fileName = "RollableBoots", menuName = "Items/Boots")]
//    public class RollableBootsItem : RollableItem
//    {
//        public override ItemType ItemType => ItemType.Boots;
//        public RollRange MovementSpeed;

//        public override void Roll(ref Item item)
//        {
//            switch (item.Rarity)
//            {
//                case Rarity.Unique:
//                case Rarity.Normal:
//                    item.BaseAffixes.Add(new Rolled { Affix = Affix.MovementSpeed, Value = MovementSpeed.Random(0, 1) });
//                    break;
//                case Rarity.Magic:
//                    item.BaseAffixes.Add(new Rolled { Affix = Affix.MovementSpeed, Value = MovementSpeed.Random(LootGenerationManager.Instance.GenerationSettings.MagicBaseMin, LootGenerationManager.Instance.GenerationSettings.MagicBaseMax) });
//                    break;
//                case Rarity.Rare:
//                    item.BaseAffixes.Add(new Rolled { Affix = Affix.MovementSpeed, Value = MovementSpeed.Random(LootGenerationManager.Instance.GenerationSettings.RareBaseMin, LootGenerationManager.Instance.GenerationSettings.RareBaseMax) });
//                    break;
//                case Rarity.Legendary:
//                    item.BaseAffixes.Add(new Rolled { Affix = Affix.MovementSpeed, Value = MovementSpeed.Random(LootGenerationManager.Instance.GenerationSettings.LegendaryBaseMin, LootGenerationManager.Instance.GenerationSettings.LegendaryBaseMax) });
//                    break;
//            }

//            base.Roll(ref item);
//        }
//    }

//    [CreateAssetMenu(fileName = "RollableLegendaryBoots", menuName = "Items/LegendaryBoots")]
//    public class RollableLegendaryBootsItem : RollableBootsItem
//    {
//        public LegendaryAffix LegendaryAffix;
//    }

//    // Legs
//    [CreateAssetMenu(fileName = "RollableLegs", menuName = "Items/Legs")]
//    public class RollableLegsItem : RollableItem
//    {
//        public override ItemType ItemType => ItemType.Legs;
//        public RollRange Armor;

//        public override void Roll(ref Item item)
//        {
//            switch (item.Rarity)
//            {
//                case Rarity.Unique:
//                case Rarity.Normal:
//                    item.BaseAffixes.Add(new Rolled { Affix = Affix.Armor, Value = Armor.Random(0, 1) });
//                    break;
//                case Rarity.Magic:
//                    item.BaseAffixes.Add(new Rolled { Affix = Affix.Armor, Value = Armor.Random(LootGenerationManager.Instance.GenerationSettings.MagicBaseMin, LootGenerationManager.Instance.GenerationSettings.MagicBaseMax) });
//                    break;
//                case Rarity.Rare:
//                    item.BaseAffixes.Add(new Rolled { Affix = Affix.Armor, Value = Armor.Random(LootGenerationManager.Instance.GenerationSettings.RareBaseMin, LootGenerationManager.Instance.GenerationSettings.RareBaseMax) });
//                    break;
//                case Rarity.Legendary:
//                    item.BaseAffixes.Add(new Rolled { Affix = Affix.Armor, Value = Armor.Random(LootGenerationManager.Instance.GenerationSettings.LegendaryBaseMin, LootGenerationManager.Instance.GenerationSettings.LegendaryBaseMax) });
//                    break;
//            }

//            base.Roll(ref item);
//        }
//    }

//    [CreateAssetMenu(fileName = "RollableLegendaryLegs", menuName = "Items/LegendaryLegs")]
//    public class RollableLegendaryLegsItem : RollableLegsItem
//    {
//        public LegendaryAffix LegendaryAffix;
//    }

//    // Chest
//    [CreateAssetMenu(fileName = "RollableChest", menuName = "Items/Chest")]
//    public class RollableChestItem : RollableItem
//    {
//        public override ItemType ItemType => ItemType.Chest;
//        public RollRange Armor;

//        public override void Roll(ref Item item)
//        {
//            switch (item.Rarity)
//            {
//                case Rarity.Unique:
//                case Rarity.Normal:
//                    item.BaseAffixes.Add(new Rolled { Affix = Affix.Armor, Value = Armor.Random(0, 1) });
//                    break;
//                case Rarity.Magic:
//                    item.BaseAffixes.Add(new Rolled { Affix = Affix.Armor, Value = Armor.Random(LootGenerationManager.Instance.GenerationSettings.MagicBaseMin, LootGenerationManager.Instance.GenerationSettings.MagicBaseMax) });
//                    break;
//                case Rarity.Rare:
//                    item.BaseAffixes.Add(new Rolled { Affix = Affix.Armor, Value = Armor.Random(LootGenerationManager.Instance.GenerationSettings.RareBaseMin, LootGenerationManager.Instance.GenerationSettings.RareBaseMax) });
//                    break;
//                case Rarity.Legendary:
//                    item.BaseAffixes.Add(new Rolled { Affix = Affix.Armor, Value = Armor.Random(LootGenerationManager.Instance.GenerationSettings.LegendaryBaseMin, LootGenerationManager.Instance.GenerationSettings.LegendaryBaseMax) });
//                    break;
//            }

//            base.Roll(ref item);
//        }
//    }

//    [CreateAssetMenu(fileName = "RollableLegendaryChest", menuName = "Items/LegendaryChest")]
//    public class RollableLegendaryChestItem : RollableChestItem
//    {
//        public LegendaryAffix LegendaryAffix;
//    }

//    // Belt
//    [CreateAssetMenu(fileName = "RollableBelt", menuName = "Items/Belt")]
//    public class RollableBeltItem : RollableItem
//    {
//        public override ItemType ItemType => ItemType.Belt;
//        public RollRange HealthRegeneration;
//        public RollRange ResourceRegeneration;

//        public override void Roll(ref Item item)
//        {
//            switch (item.Rarity)
//            {
//                case Rarity.Unique:
//                case Rarity.Normal:
//                    item.BaseAffixes.Add(new Rolled { Affix = Affix.HealthRegeneration, Value = HealthRegeneration.Random(0, 1) });
//                    item.BaseAffixes.Add(new Rolled { Affix = Affix.ResourceRegeneration, Value = ResourceRegeneration.Random(0, 1) });
//                    break;
//                case Rarity.Magic:
//                    item.BaseAffixes.Add(new Rolled { Affix = Affix.HealthRegeneration, Value = HealthRegeneration.Random(LootGenerationManager.Instance.GenerationSettings.MagicBaseMin, LootGenerationManager.Instance.GenerationSettings.MagicBaseMax) });
//                    item.BaseAffixes.Add(new Rolled { Affix = Affix.ResourceRegeneration, Value = ResourceRegeneration.Random(LootGenerationManager.Instance.GenerationSettings.MagicBaseMin, LootGenerationManager.Instance.GenerationSettings.MagicBaseMax) });
//                    break;
//                case Rarity.Rare:
//                    item.BaseAffixes.Add(new Rolled { Affix = Affix.HealthRegeneration, Value = HealthRegeneration.Random(LootGenerationManager.Instance.GenerationSettings.RareBaseMin, LootGenerationManager.Instance.GenerationSettings.RareBaseMax) });
//                    item.BaseAffixes.Add(new Rolled { Affix = Affix.ResourceRegeneration, Value = ResourceRegeneration.Random(LootGenerationManager.Instance.GenerationSettings.RareBaseMin, LootGenerationManager.Instance.GenerationSettings.RareBaseMax) });
//                    break;
//                case Rarity.Legendary:
//                    item.BaseAffixes.Add(new Rolled { Affix = Affix.HealthRegeneration, Value = HealthRegeneration.Random(LootGenerationManager.Instance.GenerationSettings.LegendaryBaseMin, LootGenerationManager.Instance.GenerationSettings.LegendaryBaseMax) });
//                    item.BaseAffixes.Add(new Rolled { Affix = Affix.ResourceRegeneration, Value = ResourceRegeneration.Random(LootGenerationManager.Instance.GenerationSettings.LegendaryBaseMin, LootGenerationManager.Instance.GenerationSettings.LegendaryBaseMax) });
//                    break;
//            }

//            base.Roll(ref item);
//        }
//    }

//    [CreateAssetMenu(fileName = "RollableLegendaryBelt", menuName = "Items/LegendaryBelt")]
//    public class RollableLegendaryBeltItem : RollableBeltItem
//    {
//        public LegendaryAffix LegendaryAffix;
//    }

//    // Hands
//    [CreateAssetMenu(fileName = "RollableHands", menuName = "Items/Hands")]
//    public class RollableHandsItem : RollableItem
//    {
//        public override ItemType ItemType => ItemType.Hands;
//    }

//    [CreateAssetMenu(fileName = "RollableLegendaryHands", menuName = "Items/LegendaryHands")]
//    public class RollableLegendaryHandsItem : RollableHandsItem
//    {
//        public LegendaryAffix LegendaryAffix;
//    }

//    // Shoulders
//    [CreateAssetMenu(fileName = "RollableShoulders", menuName = "Items/Shoulders")]
//    public class RollableShouldersItem : RollableItem
//    {
//        public override ItemType ItemType => ItemType.Shoulders;
//    }

//    [CreateAssetMenu(fileName = "RollableLegendaryShoulders", menuName = "Items/LegendaryShoulders")]
//    public class RollableLegendaryShouldersItem : RollableShouldersItem
//    {
//        public LegendaryAffix LegendaryAffix;
//    }

//    // Amulet
//    [CreateAssetMenu(fileName = "RollableAmulet", menuName = "Items/Amulet")]
//    public class RollableAmuletItem : RollableItem
//    {
//        public override ItemType ItemType => ItemType.Amulet;
//    }

//    [CreateAssetMenu(fileName = "RollableLegendaryAmulet", menuName = "Items/LegendaryAmulet")]
//    public class RollableLegendaryAmuletItem : RollableAmuletItem
//    {
//        public LegendaryAffix LegendaryAffix;
//    }

//    // Ring
//    [CreateAssetMenu(fileName = "RollableRing", menuName = "Items/Ring")]
//    public class RollableRingItem : RollableItem
//    {
//        public override ItemType ItemType => ItemType.Ring;
//    }

//    [CreateAssetMenu(fileName = "RollableLegendaryRing", menuName = "Items/LegendaryRing")]
//    public class RollableLegendaryRingItem : RollableRingItem
//    {
//        public LegendaryAffix LegendaryAffix;
//    }

//    [CreateAssetMenu(fileName = "RollableOffhand", menuName = "Items/Offhand")]
//    public class RollableOffhandItem : RollableItem
//    {
//        public override ItemType ItemType => ItemType.Offhand;
//    }

//    [CreateAssetMenu(fileName = "RollableLegendaryOffhand", menuName = "Items/LegendaryOffhand")]
//    public class RollableLegendaryOffhandItem : RollableOffhandItem
//    {
//        public LegendaryAffix LegendaryAffix;
//    }

//    [Serializable]
//    public class Item : Loottables.Loot
//    {
//        public RollableItem BaseItem;
//        public Rarity Rarity;
//        public List<Rolled> BaseAffixes;
//        public List<Rolled> RollableAffixes;
//        public ItemType ItemType => BaseItem.ItemType;
//    }
//}

//namespace Loottables
//{
//    public abstract class Lootable : ScriptableObject
//    {
//        public abstract List<Loot> Explore();
//    }

//    public enum LootType
//    {
//        Item,
//        Gold
//    }

//    public class QuantifiableLoot : Lootable
//    {
//        public LootType LootType;
//        public int MinQuantity;

//        public int MaxQuantity;

//        public int StepSize;

//        public override List<Loot> Explore()
//        {
//            var quantity = UnityEngine.Random.Range(MinQuantity, MaxQuantity + 1);
//            var mod = quantity % StepSize;
//            quantity = quantity - mod;
//            var loot = new Loot{LootType = LootType, Quantity = math.max(MinQuantity, quantity)};
//            return new List<Loot>{loot};
//        }
//    }

//    public enum LootTableMode
//    {
//        And, // each entry rolls independently; Value = chance (0-1)
//        Or   // pick exactly one entry; Value = weight
//    }

//    [Serializable]
//    public class LootEntry
//    {
//        public Lootable Entry;
//        [Tooltip("And mode: chance 0-1. Or mode: relative weight.")]
//        public float Value = 1f;
//    }

//    [CreateAssetMenu(fileName = "lootTable", menuName = "Loot/LootTable")]
//    public class LootTable : Lootable
//    {
//        public string Name;
//        public LootTableMode Mode;
//        public List<LootEntry> Entries;

//        public override List<Loot> Explore()
//        {
//            var results = new List<Loot>();
//            if (Entries.Count == 0) return results;

//            switch (Mode)
//            {
//                case LootTableMode.And:
//                    foreach (var entry in Entries)
//                    {
//                        if (UnityEngine.Random.value < entry.Value)
//                            results.AddRange(entry.Entry.Explore());
//                    }
//                    break;

//                case LootTableMode.Or:
//                    var totalWeight = Entries.Sum(e => e.Value);
//                    var roll = UnityEngine.Random.Range(0f, totalWeight);
//                    var cumulative = 0f;
//                    foreach (var entry in Entries)
//                    {
//                        cumulative += entry.Value;
//                        if (roll < cumulative)
//                            return entry.Entry.Explore();
//                    }
//                    break;
//            }

//            return results;
//        }
//    }

//    [Serializable]
//    public class Loot
//    {
//        public LootType LootType;
//        public int Quantity;
//    }
//}


