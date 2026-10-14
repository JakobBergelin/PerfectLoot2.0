using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueBelt", menuName = "Items/Unique/Belt")]
    public class RollableUniqueBeltItem : RollableBeltItem
    {
        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
