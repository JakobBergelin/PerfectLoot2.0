using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableLegendaryChest", menuName = "Items/Legendary/Chest")]
    public class RollableLegendaryChestItem : RollableChestItem, ILegendaryItem
    {
        [Tooltip("The item's special effect, applied while it is equipped")]
        public LegendaryPower LegendaryPower;
        LegendaryPower ILegendaryItem.Power => LegendaryPower;

        public override Rarity? ForcedRarity => Rarity.Legendary;
    }
}
