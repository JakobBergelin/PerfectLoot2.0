using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueAmulet", menuName = "Items/Unique/Amulet")]
    public class RollableUniqueAmuletItem : RollableAmuletItem
    {
        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
