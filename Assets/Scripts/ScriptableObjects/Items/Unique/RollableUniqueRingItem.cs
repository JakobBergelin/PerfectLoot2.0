using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueRing", menuName = "Items/UniqueRing")]
    public class RollableUniqueRingItem : RollableRingItem
    {
        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
