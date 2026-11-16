using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableOffhand", menuName = "Items/Offhand")]
    public class RollableOffhandItem : RollableItem
    {
        public override ItemType ItemType => ItemType.Offhand;
        [Tooltip("What kind of offhand it is (one kind), for skills that need one. Uniques and legendaries: the kind of their base item.")]
        public GearKinds Kind;
        public override GearKinds Gear => Kind;
    }
}
