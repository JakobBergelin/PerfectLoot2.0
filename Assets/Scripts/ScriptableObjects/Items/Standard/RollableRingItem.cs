using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableRing", menuName = "Items/Ring")]
    public class RollableRingItem : RollableItem
    {
        public override ItemType ItemType => ItemType.Ring;
    }
}
