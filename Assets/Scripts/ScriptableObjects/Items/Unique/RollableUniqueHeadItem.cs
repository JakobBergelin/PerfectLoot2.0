using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueHead", menuName = "Items/Unique/Head")]
    public class RollableUniqueHeadItem : RollableHeadItem, ILegendaryItem
    {
        [Tooltip("The item's special effect, applied while it is equipped")]
        public LegendaryPower LegendaryPower;
        LegendaryPower ILegendaryItem.Power => LegendaryPower;

        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
