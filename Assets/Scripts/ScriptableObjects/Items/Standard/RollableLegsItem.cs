using System.Collections.Generic;
using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableLegs", menuName = "Items/Legs")]
    public class RollableLegsItem : RollableItem
    {
        public override ItemType ItemType => ItemType.Legs;
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
