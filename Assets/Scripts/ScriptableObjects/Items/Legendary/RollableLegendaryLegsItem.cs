using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableLegendaryLegs", menuName = "Items/Legendary/Legs")]
    public class RollableLegendaryLegsItem : RollableLegsItem, ILegendaryItem
    {
        [Tooltip("The item's special effect, applied while it is equipped")]
        public LegendaryPower LegendaryPower;
        LegendaryPower ILegendaryItem.Power => LegendaryPower;

        public override Rarity? ForcedRarity => Rarity.Legendary;
    }
}
