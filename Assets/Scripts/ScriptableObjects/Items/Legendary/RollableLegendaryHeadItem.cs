using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableLegendaryHead", menuName = "Items/Legendary/Head")]
    public class RollableLegendaryHeadItem : RollableHeadItem, ILegendaryItem
    {
        [Tooltip("The item's special effect, applied while it is equipped")]
        public LegendaryPower LegendaryPower;
        LegendaryPower ILegendaryItem.Power => LegendaryPower;

        public override Rarity? ForcedRarity => Rarity.Legendary;
    }
}
