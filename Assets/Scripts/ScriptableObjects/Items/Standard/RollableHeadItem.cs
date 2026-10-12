using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableHead", menuName = "Items/Head")]
    public class RollableHeadItem : RollableItem
    {
        public override ItemType ItemType => ItemType.Head;
    }
}
