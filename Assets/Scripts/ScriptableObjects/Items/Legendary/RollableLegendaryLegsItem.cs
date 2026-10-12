using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableLegendaryLegs", menuName = "Items/LegendaryLegs")]
    public class RollableLegendaryLegsItem : RollableLegsItem
    {
        public LegendaryAffix LegendaryAffix;

        public override Rarity? ForcedRarity => Rarity.Legendary;
    }
}
