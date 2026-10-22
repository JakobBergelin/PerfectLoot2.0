using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableLegendaryOffhand", menuName = "Items/Legendary/Offhand")]
    public class RollableLegendaryOffhandItem : RollableOffhandItem, ILegendaryItem
    {
        [Tooltip("The item's special effect, applied while it is equipped")]
        public LegendaryPower LegendaryPower;
        LegendaryPower ILegendaryItem.Power => LegendaryPower;

        public override Rarity? ForcedRarity => Rarity.Legendary;
    }
}
