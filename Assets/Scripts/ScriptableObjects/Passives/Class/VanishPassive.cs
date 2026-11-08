using UnityEngine;

namespace Passives
{
    /// <summary>Rogue's class passive: after a Mobility skill, enemies lose track of you unless you hit them.</summary>
    [CreateAssetMenu(fileName = "Vanish", menuName = "Passives/Class/Vanish")]
    public class VanishPassive : PassiveSkill<Vanish>
    {
        [Tooltip("Seconds enemies lose track of you")]
        [Min(0f)] public float Duration = 2f;

        public override PassiveType Type => PassiveType.Vanish;

        protected override Vanish CreateComponent() => new Vanish { Duration = Duration };
    }
}
