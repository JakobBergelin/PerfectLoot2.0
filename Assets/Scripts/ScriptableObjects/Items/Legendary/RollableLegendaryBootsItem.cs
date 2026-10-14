using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableLegendaryBoots", menuName = "Items/Legendary/Boots")]
    public class RollableLegendaryBootsItem : RollableBootsItem
    {
        public LegendaryAffix LegendaryAffix;

        public override Rarity? ForcedRarity => Rarity.Legendary;
    }
}
