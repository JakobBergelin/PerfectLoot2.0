using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueHead", menuName = "Items/Unique/Head")]
    public class RollableUniqueHeadItem : RollableHeadItem
    {
        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
