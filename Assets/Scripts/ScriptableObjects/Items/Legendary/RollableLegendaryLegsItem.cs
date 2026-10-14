using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableLegendaryLegs", menuName = "Items/Legendary/Legs")]
    public class RollableLegendaryLegsItem : RollableLegsItem
    {
        public LegendaryAffix LegendaryAffix;

        public override Rarity? ForcedRarity => Rarity.Legendary;
    }
}
