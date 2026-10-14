using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableLegendaryWeapon", menuName = "Items/Legendary/Weapon")]
    public class RollableLegendaryWeaponItem : RollableWeaponItem
    {
        public LegendaryAffix LegendaryAffix;

        public override Rarity? ForcedRarity => Rarity.Legendary;
    }
}
