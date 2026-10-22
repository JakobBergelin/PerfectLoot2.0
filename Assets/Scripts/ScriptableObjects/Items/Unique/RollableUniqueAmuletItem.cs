using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueAmulet", menuName = "Items/Unique/Amulet")]
    public class RollableUniqueAmuletItem : RollableAmuletItem, ILegendaryItem
    {
        [Tooltip("The item's special effect, applied while it is equipped")]
        public LegendaryPower LegendaryPower;
        LegendaryPower ILegendaryItem.Power => LegendaryPower;

        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
