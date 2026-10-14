using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueOffhand", menuName = "Items/Unique/Offhand")]
    public class RollableUniqueOffhandItem : RollableOffhandItem
    {
        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
