using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueLegs", menuName = "Items/Unique/Legs")]
    public class RollableUniqueLegsItem : RollableLegsItem, ILegendaryItem
    {
        [Tooltip("The item's special effect, applied while it is equipped")]
        public LegendaryPower LegendaryPower;
        LegendaryPower ILegendaryItem.Power => LegendaryPower;

        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
