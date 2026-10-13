using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueBelt", menuName = "Items/UniqueBelt")]
    public class RollableUniqueBeltItem : RollableBeltItem
    {
        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
