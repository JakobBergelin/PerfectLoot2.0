using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueBoots", menuName = "Items/UniqueBoots")]
    public class RollableUniqueBootsItem : RollableBootsItem
    {
        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
