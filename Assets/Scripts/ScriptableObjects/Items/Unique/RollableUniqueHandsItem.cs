using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueHands", menuName = "Items/Unique/Hands")]
    public class RollableUniqueHandsItem : RollableHandsItem, ILegendaryItem
    {
        [Tooltip("The item's special effect, applied while it is equipped")]
        public LegendaryPower LegendaryPower;
        LegendaryPower ILegendaryItem.Power => LegendaryPower;

        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
