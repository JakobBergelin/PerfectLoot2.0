using System.Collections.Generic;
using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableChest", menuName = "Items/Chest")]
    public class RollableChestItem : RollableItem
    {
        public override ItemType ItemType => ItemType.Chest;
        public RollRange Armor;

        protected override IEnumerable<(Affix Affix, RollRange Range)> IntrinsicAffixes
        {
            get
            {
            yield return (Affix.Armor, Armor);
            }
        }
    }
}
