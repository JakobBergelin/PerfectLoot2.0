using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableLegendaryBelt", menuName = "Items/Legendary/Belt")]
    public class RollableLegendaryBeltItem : RollableBeltItem
    {
        public LegendaryAffix LegendaryAffix;

        public override Rarity? ForcedRarity => Rarity.Legendary;
    }
}
