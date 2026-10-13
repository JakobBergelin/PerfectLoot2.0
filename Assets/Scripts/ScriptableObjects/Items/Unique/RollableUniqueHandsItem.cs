using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueHands", menuName = "Items/UniqueHands")]
    public class RollableUniqueHandsItem : RollableHandsItem
    {
        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
