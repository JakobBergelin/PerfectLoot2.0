using System.Collections.Generic;
using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableBoots", menuName = "Items/Boots")]
    public class RollableBootsItem : RollableItem
    {
        public override ItemType ItemType => ItemType.Boots;
        public RollRange MovementSpeed;

        protected override IEnumerable<(Affix Affix, RollRange Range)> IntrinsicAffixes
        {
            get
            {
            yield return (Affix.MovementSpeed, MovementSpeed);
            }
        }
    }
}
