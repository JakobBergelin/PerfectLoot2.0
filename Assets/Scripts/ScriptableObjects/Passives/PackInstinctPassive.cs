using UnityEngine;

namespace Passives
{
    /// <summary>Raises the critical chance of allies near the wearer, itself included. Several wearers don't stack.</summary>
    [CreateAssetMenu(fileName = "PackInstinct", menuName = "Passives/Pack Instinct")]
    public class PackInstinctPassive : PassiveSkill<PackInstinct>
    {
        [Tooltip("Meters from the wearer")]
        public float Radius = 8f;
        [Tooltip("Added to critical chance, 0.1 = +10%")]
        public float CriticalChanceBonus = 0.1f;

        public override PassiveType Type => PassiveType.PackInstinct;

        protected override PackInstinct CreateComponent() => new PackInstinct
        {
            Radius = Radius,
            CriticalChanceBonus = CriticalChanceBonus,
        };
    }
}
