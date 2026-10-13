using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueShoulders", menuName = "Items/UniqueShoulders")]
    public class RollableUniqueShouldersItem : RollableShouldersItem
    {
        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
