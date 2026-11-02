using UnityEngine;

namespace Passives
{
    /// <summary>Cooldown reduction from Agility, with diminishing returns (the same curve as Dash's own Agility scaling).</summary>
    [CreateAssetMenu(fileName = "Quickening", menuName = "Passives/Quickening")]
    public class QuickeningPassive : PassiveSkill<Quickening>
    {
        [Tooltip("Cooldown reduction gained is Agility × this / (1 + Agility × this): 0.005 = +33% at 100 Agility, +50% at 200")]
        public float CooldownPerAgility = 0.005f;

        public override PassiveType Type => PassiveType.Quickening;

        protected override Quickening CreateComponent() => new Quickening { CooldownPerAgility = CooldownPerAgility };
    }
}
