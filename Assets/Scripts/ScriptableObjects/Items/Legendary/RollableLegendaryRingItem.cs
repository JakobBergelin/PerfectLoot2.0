using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableLegendaryRing", menuName = "Items/Legendary/Ring")]
    public class RollableLegendaryRingItem : RollableRingItem
    {
        public LegendaryAffix LegendaryAffix;

        public override Rarity? ForcedRarity => Rarity.Legendary;
    }
}
