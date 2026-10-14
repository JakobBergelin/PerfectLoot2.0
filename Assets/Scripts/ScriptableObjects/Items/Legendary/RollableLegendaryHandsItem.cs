using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableLegendaryHands", menuName = "Items/Legendary/Hands")]
    public class RollableLegendaryHandsItem : RollableHandsItem
    {
        public LegendaryAffix LegendaryAffix;

        public override Rarity? ForcedRarity => Rarity.Legendary;
    }
}
