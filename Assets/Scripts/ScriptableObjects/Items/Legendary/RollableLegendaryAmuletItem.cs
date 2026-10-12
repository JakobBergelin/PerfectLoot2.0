using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableLegendaryAmulet", menuName = "Items/LegendaryAmulet")]
    public class RollableLegendaryAmuletItem : RollableAmuletItem
    {
        public LegendaryAffix LegendaryAffix;
    }
}
