using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueRing", menuName = "Items/Unique/Ring")]
    public class RollableUniqueRingItem : RollableRingItem
    {
        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
