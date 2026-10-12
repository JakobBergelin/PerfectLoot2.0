using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableOffhand", menuName = "Items/Offhand")]
    public class RollableOffhandItem : RollableItem
    {
        public override ItemType ItemType => ItemType.Offhand;
    }
}
