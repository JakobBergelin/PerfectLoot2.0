using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueChest", menuName = "Items/UniqueChest")]
    public class RollableUniqueChestItem : RollableChestItem
    {
        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
