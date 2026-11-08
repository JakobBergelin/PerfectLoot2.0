using UnityEngine;

namespace Passives
{
    /// <summary>Fighter's class passive: melee attacks raise block chance for a moment.</summary>
    [CreateAssetMenu(fileName = "Brace", menuName = "Passives/Class/Brace")]
    public class BracePassive : PassiveSkill<Brace>
    {
        [Tooltip("Block chance added, 0.2 = +20%")]
        [Min(0f)] public float BlockBonus = 0.2f;
        [Tooltip("Seconds it lasts after each melee attack")]
        [Min(0f)] public float Duration = 2f;

        public override PassiveType Type => PassiveType.Brace;

        protected override Brace CreateComponent() => new Brace { BlockBonus = BlockBonus, Duration = Duration };
    }
}
