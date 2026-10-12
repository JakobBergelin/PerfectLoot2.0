using System.Collections.Generic;
using UnityEngine;

namespace Loottables
{
    [CreateAssetMenu(fileName = "QuantifiableLoot", menuName = "Loot/QuantifiableLoot")]
    public class QuantifiableLoot : Lootable
    {
        public LootType LootType;
        public int MinQuantity;
        public int MaxQuantity;
        public int StepSize = 1;

        public override List<Loot> Explore()
        {
            var step = Mathf.Max(1, StepSize); // 0 would throw a divide-by-zero
            var quantity = Random.Range(MinQuantity, MaxQuantity + 1);
            quantity -= quantity % step;
            var loot = new Loot { LootType = LootType, Quantity = Mathf.Max(MinQuantity, quantity) };
            return new List<Loot> { loot };
        }
    }
}
