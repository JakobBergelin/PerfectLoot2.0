using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueLegs", menuName = "Items/Unique/Legs")]
    public class RollableUniqueLegsItem : RollableLegsItem
    {
        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
