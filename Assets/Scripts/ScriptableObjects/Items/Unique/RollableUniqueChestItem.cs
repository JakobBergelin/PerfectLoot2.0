using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueChest", menuName = "Items/Unique/Chest")]
    public class RollableUniqueChestItem : RollableChestItem, ILegendaryItem
    {
        [Tooltip("The item's special effect, applied while it is equipped")]
        public LegendaryPower LegendaryPower;
        LegendaryPower ILegendaryItem.Power => LegendaryPower;

        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
