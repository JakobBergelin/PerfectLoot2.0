using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueOffhand", menuName = "Items/UniqueOffhand")]
    public class RollableUniqueOffhandItem : RollableOffhandItem
    {
        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
