using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableLegendaryAmulet", menuName = "Items/Legendary/Amulet")]
    public class RollableLegendaryAmuletItem : RollableAmuletItem
    {
        public LegendaryAffix LegendaryAffix;

        public override Rarity? ForcedRarity => Rarity.Legendary;
    }
}
