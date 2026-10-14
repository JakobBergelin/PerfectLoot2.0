using UnityEngine;

namespace Equipment
{
    [CreateAssetMenu(fileName = "RollableUniqueShoulders", menuName = "Items/Unique/Shoulders")]
    public class RollableUniqueShouldersItem : RollableShouldersItem
    {
        public override Rarity? ForcedRarity => Rarity.Unique;
    }
}
