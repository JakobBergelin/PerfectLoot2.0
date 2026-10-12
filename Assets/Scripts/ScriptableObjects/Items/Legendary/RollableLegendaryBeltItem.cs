using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableLegendaryBelt", menuName = "Items/LegendaryBelt")]
    public class RollableLegendaryBeltItem : RollableBeltItem
    {
        public LegendaryAffix LegendaryAffix;

        public override Rarity? ForcedRarity => Rarity.Legendary;
    }
}
