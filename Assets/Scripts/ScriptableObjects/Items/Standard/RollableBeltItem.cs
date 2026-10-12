using System.Collections.Generic;
using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableBelt", menuName = "Items/Belt")]
    public class RollableBeltItem : RollableItem
    {
        public override ItemType ItemType => ItemType.Belt;
        public RollRange HealthRegeneration;
        public RollRange ResourceRegeneration;

        protected override IEnumerable<(Affix Affix, RollRange Range)> IntrinsicAffixes
        {
            get
            {
            yield return (Affix.HealthRegeneration, HealthRegeneration);
            yield return (Affix.ResourceRegeneration, ResourceRegeneration);
            }
        }
    }
}
