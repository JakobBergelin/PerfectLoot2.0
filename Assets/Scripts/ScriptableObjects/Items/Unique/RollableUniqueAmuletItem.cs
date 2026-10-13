using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueAmulet", menuName = "Items/UniqueAmulet")]
    public class RollableUniqueAmuletItem : RollableAmuletItem
    {
        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
