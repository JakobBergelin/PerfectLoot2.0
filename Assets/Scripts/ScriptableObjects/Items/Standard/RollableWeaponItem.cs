using System.Collections.Generic;
using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableWeapon", menuName = "Items/Weapon")]
    public class RollableWeaponItem : RollableItem
    {
        public override ItemType ItemType => ItemType.Weapon;
        public RollRange WeaponDamage;
        [Tooltip("Multiplies the time between attacks: 0.9 is faster, 1.1 is slower")]
        public float WeaponCooldown = 1f;
        [Tooltip("Damage rolls with this weapon are WeaponDamage +- this")]
        public float WeaponDamageRange;
        [Tooltip("How far in front of the wielder melee attacks land")]
        public float WeaponReach = 1.1f;

        protected override IEnumerable<(Affix Affix, RollRange Range)> IntrinsicAffixes
        {
            get
            {
            yield return (Affix.WeaponDamage, WeaponDamage);
            }
        }

        public override void AddFixedStats(ref StatBlock stats)
        {
            stats.WeaponDamageRange += WeaponDamageRange;
            stats.WeaponReach += WeaponReach;
            stats.WeaponCooldown += WeaponCooldown;
        }
    }
}
