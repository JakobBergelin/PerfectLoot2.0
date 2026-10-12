using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableLegendaryBoots", menuName = "Items/LegendaryBoots")]
    public class RollableLegendaryBootsItem : RollableBootsItem
    {
        public LegendaryAffix LegendaryAffix;

        public override Rarity? ForcedRarity => Rarity.Legendary;
    }
}
