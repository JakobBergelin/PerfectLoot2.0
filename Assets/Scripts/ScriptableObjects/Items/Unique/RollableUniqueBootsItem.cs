using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueBoots", menuName = "Items/Unique/Boots")]
    public class RollableUniqueBootsItem : RollableBootsItem, ILegendaryItem
    {
        [Tooltip("The item's special effect, applied while it is equipped")]
        public LegendaryPower LegendaryPower;
        LegendaryPower ILegendaryItem.Power => LegendaryPower;

        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
