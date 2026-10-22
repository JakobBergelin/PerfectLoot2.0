using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableLegendaryBelt", menuName = "Items/Legendary/Belt")]
    public class RollableLegendaryBeltItem : RollableBeltItem, ILegendaryItem
    {
        [Tooltip("The item's special effect, applied while it is equipped")]
        public LegendaryPower LegendaryPower;
        LegendaryPower ILegendaryItem.Power => LegendaryPower;

        public override Rarity? ForcedRarity => Rarity.Legendary;
    }
}
