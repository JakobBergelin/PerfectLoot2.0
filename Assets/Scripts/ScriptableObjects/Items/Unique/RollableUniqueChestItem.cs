using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueChest", menuName = "Items/Unique/Chest")]
    public class RollableUniqueChestItem : RollableChestItem
    {
        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
