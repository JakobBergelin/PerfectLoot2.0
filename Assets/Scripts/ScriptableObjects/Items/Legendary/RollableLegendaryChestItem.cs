using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableLegendaryChest", menuName = "Items/Legendary/Chest")]
    public class RollableLegendaryChestItem : RollableChestItem
    {
        public LegendaryAffix LegendaryAffix;

        public override Rarity? ForcedRarity => Rarity.Legendary;
    }
}
