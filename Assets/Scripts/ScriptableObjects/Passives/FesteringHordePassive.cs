using UnityEngine;

namespace Passives
{
    /// <summary>Raises the Poison resistance of allies near the wearer, itself included. Several wearers stack.</summary>
    [CreateAssetMenu(fileName = "FesteringHorde", menuName = "Passives/Festering Horde")]
    public class FesteringHordePassive : PassiveSkill<FesteringHorde>
    {
        [Tooltip("Meters from the wearer")]
        public float Radius = 8f;
        [Tooltip("Added to Poison resistance by each wearer")]
        public float PoisonResistance = 5f;

        public override PassiveType Type => PassiveType.FesteringHorde;

        protected override FesteringHorde CreateComponent() => new FesteringHorde
        {
            Radius = Radius,
            PoisonResistance = PoisonResistance,
        };
    }
}
