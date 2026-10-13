using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueWeapon", menuName = "Items/UniqueWeapon")]
    public class RollableUniqueWeaponItem : RollableWeaponItem
    {
        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
