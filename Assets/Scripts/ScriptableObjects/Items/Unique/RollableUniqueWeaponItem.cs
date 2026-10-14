using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueWeapon", menuName = "Items/Unique/Weapon")]
    public class RollableUniqueWeaponItem : RollableWeaponItem
    {
        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
