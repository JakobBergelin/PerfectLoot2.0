using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueShoulders", menuName = "Items/Unique/Shoulders")]
    public class RollableUniqueShouldersItem : RollableShouldersItem, ILegendaryItem
    {
        [Tooltip("The item's special effect, applied while it is equipped")]
        public LegendaryPower LegendaryPower;
        LegendaryPower ILegendaryItem.Power => LegendaryPower;

        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
