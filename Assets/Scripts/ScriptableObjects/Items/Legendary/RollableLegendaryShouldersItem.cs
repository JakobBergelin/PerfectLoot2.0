using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableLegendaryShoulders", menuName = "Items/LegendaryShoulders")]
    public class RollableLegendaryShouldersItem : RollableShouldersItem
    {
        public LegendaryAffix LegendaryAffix;

        public override Rarity? ForcedRarity => Rarity.Legendary;
    }
}
