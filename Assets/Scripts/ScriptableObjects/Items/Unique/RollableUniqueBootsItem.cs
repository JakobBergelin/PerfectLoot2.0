using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueBoots", menuName = "Items/Unique/Boots")]
    public class RollableUniqueBootsItem : RollableBootsItem
    {
        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
