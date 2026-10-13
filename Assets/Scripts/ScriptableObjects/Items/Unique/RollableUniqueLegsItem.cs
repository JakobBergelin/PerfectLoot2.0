using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueLegs", menuName = "Items/UniqueLegs")]
    public class RollableUniqueLegsItem : RollableLegsItem
    {
        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
