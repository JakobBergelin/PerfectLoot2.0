

using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

namespace Loottables
{
    public abstract class Lootable
    {
        public abstract List<Loot> Explore();
    }

    public class Item : Lootable
    {
        public string Name;
        public int MinQuantity;

        public int MaxQuantity;

        public int StepSize;

        public override List<Loot> Explore()
        {
            var quantity = UnityEngine.Random.Range(MinQuantity, MaxQuantity);
            var mod = quantity % StepSize;
            quantity = quantity - mod;
            var loot = new Loot{Name = Name, Quantity = math.max(MinQuantity, quantity)};
            return new List<Loot>{loot};
        }
    }

    public class EntryAnd 
    {
        public Lootable Entry;
        public float Chance;
    }

    public class EntryOr
    {
        public Lootable Entry;
        public float Weight;
    }

    public class Table : Lootable
    {
        public string Name;
        public List<EntryAnd> AndEntries;
        public List<EntryOr> OrEntries;

        public override List<Loot> Explore()
        {
            if(AndEntries.Count > 0)
            {
                var items = new List<Loot>();
                foreach (var entry in AndEntries)
                {
                    var roll = UnityEngine.Random.Range(0.0f,1.0f);
                    if(roll < entry.Chance)
                    {
                        items.AddRange(entry.Entry.Explore());
                    }
                }
                return items;
            }
            else if(OrEntries.Count > 0)
            {
                var totalWeight = OrEntries.Sum(i => i.Weight);
                var randomValue = UnityEngine.Random.Range(0.0f, totalWeight);
                var cumulativeWeight = 0.0f;
                foreach(var item in OrEntries)
                {
                    cumulativeWeight += item.Weight;
                    if(randomValue < cumulativeWeight)
                    {
                        return item.Entry.Explore();
                    }
                }
            }
            return new List<Loot>();
        }
    }

    public class Loot
    {
        public string Name;
        public int Quantity;
    }
}


