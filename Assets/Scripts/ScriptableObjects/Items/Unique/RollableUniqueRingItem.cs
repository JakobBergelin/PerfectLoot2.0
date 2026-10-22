using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueRing", menuName = "Items/Unique/Ring")]
    public class RollableUniqueRingItem : RollableRingItem, ILegendaryItem
    {
        [Tooltip("The item's special effect, applied while it is equipped")]
        public LegendaryPower LegendaryPower;
        LegendaryPower ILegendaryItem.Power => LegendaryPower;

        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
