using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableAmulet", menuName = "Items/Amulet")]
    public class RollableAmuletItem : RollableItem
    {
        public override ItemType ItemType => ItemType.Amulet;
    }
}
