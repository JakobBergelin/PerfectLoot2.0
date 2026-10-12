using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableHands", menuName = "Items/Hands")]
    public class RollableHandsItem : RollableItem
    {
        public override ItemType ItemType => ItemType.Hands;
    }
}
