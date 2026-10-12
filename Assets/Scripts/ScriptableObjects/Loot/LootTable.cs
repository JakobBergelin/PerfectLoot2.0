using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Loottables
{
    [CreateAssetMenu(fileName = "lootTable", menuName = "Loot/LootTable")]
    public class LootTable : Lootable
    {
        public string Name;
        public LootTableMode Mode;
        public List<LootEntry> Entries;

        public override List<Loot> Explore()
        {
            var results = new List<Loot>();
            if (Entries == null || Entries.Count == 0) return results;

            switch (Mode)
            {
                case LootTableMode.And:
                    foreach (var entry in Entries)
                    {
                        if (UnityEngine.Random.value < entry.Value)
                            results.AddRange(entry.Entry.Explore());
                    }
                    break;

                case LootTableMode.Or:
                    var totalWeight = Entries.Sum(e => e.Value);
                    var roll = UnityEngine.Random.Range(0f, totalWeight);
                    var cumulative = 0f;
                    foreach (var entry in Entries)
                    {
                        cumulative += entry.Value;
                        if (roll < cumulative)
                            return entry.Entry.Explore();
                    }
                    break;
            }

            return results;
        }
    }
}
