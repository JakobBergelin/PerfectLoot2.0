using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableShoulders", menuName = "Items/Shoulders")]
    public class RollableShouldersItem : RollableItem
    {
        public override ItemType ItemType => ItemType.Shoulders;
    }
}
