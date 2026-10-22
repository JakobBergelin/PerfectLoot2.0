using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueWeapon", menuName = "Items/Unique/Weapon")]
    public class RollableUniqueWeaponItem : RollableWeaponItem, ILegendaryItem
    {
        [Tooltip("The item's special effect, applied while it is equipped")]
        public LegendaryPower LegendaryPower;
        LegendaryPower ILegendaryItem.Power => LegendaryPower;

        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
