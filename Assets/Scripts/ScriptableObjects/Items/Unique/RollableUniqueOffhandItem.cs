using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueOffhand", menuName = "Items/Unique/Offhand")]
    public class RollableUniqueOffhandItem : RollableOffhandItem, ILegendaryItem
    {
        [Tooltip("The item's special effect, applied while it is equipped")]
        public LegendaryPower LegendaryPower;
        LegendaryPower ILegendaryItem.Power => LegendaryPower;

        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
