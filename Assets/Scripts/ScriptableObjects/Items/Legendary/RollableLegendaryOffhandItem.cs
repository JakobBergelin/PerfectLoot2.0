using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableLegendaryOffhand", menuName = "Items/LegendaryOffhand")]
    public class RollableLegendaryOffhandItem : RollableOffhandItem
    {
        public LegendaryAffix LegendaryAffix;
    }
}
