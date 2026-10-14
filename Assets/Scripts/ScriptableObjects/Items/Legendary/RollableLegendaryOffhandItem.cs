using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableLegendaryOffhand", menuName = "Items/Legendary/Offhand")]
    public class RollableLegendaryOffhandItem : RollableOffhandItem
    {
        public LegendaryAffix LegendaryAffix;

        public override Rarity? ForcedRarity => Rarity.Legendary;
    }
}
