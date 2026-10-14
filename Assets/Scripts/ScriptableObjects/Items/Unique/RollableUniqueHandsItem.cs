using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueHands", menuName = "Items/Unique/Hands")]
    public class RollableUniqueHandsItem : RollableHandsItem
    {
        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
