using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueHead", menuName = "Items/UniqueHead")]
    public class RollableUniqueHeadItem : RollableHeadItem
    {
        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
