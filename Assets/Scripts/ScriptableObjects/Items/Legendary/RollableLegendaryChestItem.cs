using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableLegendaryChest", menuName = "Items/LegendaryChest")]
    public class RollableLegendaryChestItem : RollableChestItem
    {
        public LegendaryAffix LegendaryAffix;
    }
}
