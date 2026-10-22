using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueBelt", menuName = "Items/Unique/Belt")]
    public class RollableUniqueBeltItem : RollableBeltItem, ILegendaryItem
    {
        [Tooltip("The item's special effect, applied while it is equipped")]
        public LegendaryPower LegendaryPower;
        LegendaryPower ILegendaryItem.Power => LegendaryPower;

        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
