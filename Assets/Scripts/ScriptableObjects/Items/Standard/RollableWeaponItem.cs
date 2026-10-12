using System.Collections.Generic;
using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableWeapon", menuName = "Items/Weapon")]
    public class RollableWeaponItem : RollableItem
    {
        public override ItemType ItemType => ItemType.Weapon;
        public RollRange WeaponDamage;
        public float WeaponCooldown;
        public float WeaponDamageRange;

        protected override IEnumerable<(Affix Affix, RollRange Range)> IntrinsicAffixes
        {
            get
            {
            yield return (Affix.WeaponDamage, WeaponDamage);
            }
        }
    }
}
